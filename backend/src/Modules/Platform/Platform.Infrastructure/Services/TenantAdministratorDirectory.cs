using Auth.Domain.Constants;
using Auth.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NWFM.Shared.Messaging;
using NWFM.Shared.Options;
using Platform.Application.Common.Interfaces;
using Platform.Application.Signup.Common;

namespace Platform.Infrastructure.Services;

/// <summary>
/// Opens a nested tenant scope so the tenant Identity store is reachable from a platform request
/// or the provisioning job. A console-made administrator obeys the same plan user limit and unique
/// e-mail rule the tenant app enforces.
/// </summary>
internal sealed class TenantAdministratorDirectory(
    IServiceScopeFactory scopeFactory,
    ITenantContext tenantContext,
    IOptions<PlatformOptions> platformOptions,
    IOptions<EmailSettings> emailOptions,
    IHostEnvironment environment,
    TimeProvider timeProvider,
    ILogger<TenantAdministratorDirectory> logger)
    : ITenantAdministratorDirectory
{
    private const string ActivatePath = "/activate";
    private const string KeyPlaceholder = "{key}";
    private const string NotAnAdministrator = "The administrator was not found in this workspace.";
    private const string LastAdministrator = "This is the workspace's last active administrator. Add or reactivate another one first.";

    public Task<IReadOnlyList<TenantAdministratorDto>> ListAsync(TenantInfo tenant, CancellationToken cancellationToken) =>
        RunAsync(tenant, async sp =>
        {
            var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var administrators = await BackOfficeAdministratorsAsync(userManager);

            return (IReadOnlyList<TenantAdministratorDto>)administrators
                .OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase)
                .Select(ToDto)
                .ToList();
        });

    /// <summary>
    /// Creates the account with no password: the credentials e-mail that follows gives it a temporary
    /// one and a set-password link.
    /// </summary>
    public Task<Result<string>> CreateAsync(TenantInfo tenant, string email, string? phoneNumber, CancellationToken cancellationToken) =>
        RunAsync(tenant, async sp =>
        {
            var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var address = email.Trim();

            if (tenant.MaxUsers is int maxUsers && userManager.Users.Count() >= maxUsers)
            {
                return PlatformResults.Fail<string>(
                    $"The workspace plan allows {maxUsers} users.", TenancyErrorCodes.PlanLimitReached, httpStatusCode: 403);
            }

            if (await userManager.FindByNameAsync(address) is not null || await userManager.FindByEmailAsync(address) is not null)
            {
                return PlatformResults.Fail<string>(
                    $"A user named '{address}' already exists in the workspace.", ApiErrorCodes.BackendBusinessRuleValidation, httpStatusCode: 409);
            }

            var user = new ApplicationUser
            {
                UserName = address,
                Email = address,
                PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim(),
                EmailConfirmed = true
            };

            Ensure(await userManager.CreateAsync(user));
            Ensure(await userManager.AddToRoleAsync(user, Roles.Administrator));

            return Result.Success(user.Id.ToString());
        });

    public Task<Result<TenantAdministratorDto>> SetEnabledAsync(TenantInfo tenant, string userId, bool isEnabled,
        CancellationToken cancellationToken) =>
        RunAsync(tenant, async sp =>
        {
            var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var administrators = await BackOfficeAdministratorsAsync(userManager);
            var user = administrators.FirstOrDefault(u => u.Id.ToString() == userId);

            if (user is null)
            {
                return PlatformResults.Fail<TenantAdministratorDto>(NotAnAdministrator, ApiErrorCodes.NotFound, httpStatusCode: 404);
            }

            if (!isEnabled && !administrators.Any(u => u.Id.ToString() != userId && !IsSuspended(u)))
            {
                return PlatformResults.Fail<TenantAdministratorDto>(LastAdministrator, ApiErrorCodes.BackendBusinessRuleValidation, httpStatusCode: 409);
            }

            // Suspension is a permanent lockout, the same as disabling the account from the tenant app.
            Ensure(await userManager.SetLockoutEnabledAsync(user, true));
            Ensure(await userManager.SetLockoutEndDateAsync(user, isEnabled ? null : DateTimeOffset.MaxValue));

            return Result<TenantAdministratorDto>.Success(ToDto(user));
        });

    public async Task<Result<TenantCredentialsDelivery>> SendCredentialsAsync(TenantCredentialsRequest request,
        CancellationToken cancellationToken)
    {
        var mail = emailOptions.Value;

        // Checked before the account is touched: rotating the password with nowhere to send it would
        // only lock the administrator out of whatever they were sent before.
        if (!mail.UseMock && !mail.Enabled)
        {
            logger.LogWarning("E-mail is switched off; credentials for a {TenantKey} administrator were not sent.", request.Tenant.Key);
            return Result<TenantCredentialsDelivery>.Success(TenantCredentialsDelivery.EmailDisabled);
        }

        return await RunAsync(request.Tenant, async sp =>
        {
            var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(request.UserId);

            if (user is null
                || string.IsNullOrWhiteSpace(user.Email)
                || !await userManager.IsInRoleAsync(user, Roles.Administrator))
            {
                return PlatformResults.Fail<TenantCredentialsDelivery>(
                    "The administrator was not found, or has no e-mail address.", ApiErrorCodes.NotFound, httpStatusCode: 404);
            }

            // The account keeps no password until the link sets one: removing it also rotates the
            // security stamp, which retires any link sent before.
            if (await userManager.HasPasswordAsync(user))
            {
                Ensure(await userManager.RemovePasswordAsync(user));
            }
            else
            {
                Ensure(await userManager.UpdateSecurityStampAsync(user));
            }

            // Issued after the password change: that change rotates the security stamp, which is
            // what retires any link sent before.
            var token = await userManager.GeneratePasswordResetTokenAsync(user);

            var options = platformOptions.Value;
            var workspaceUrl = options.TenantAppUrlTemplate.Replace(KeyPlaceholder, request.Tenant.Key, StringComparison.Ordinal).TrimEnd('/');
            var link = $"{workspaceUrl}{ActivatePath}?uid={Uri.EscapeDataString(user.Id.ToString())}&token={Uri.EscapeDataString(token)}";
            var userName = user.UserName ?? user.Email;

            var email = request.Email == TenantCredentialsEmail.NewWorkspace
                ? SignupEmails.Activation(request.Language, request.OrganizationName, workspaceUrl, userName, link, options.AdminActivationHours)
                : SignupEmails.AdministratorCredentials(request.Language, request.OrganizationName, workspaceUrl, userName, link, options.AdminActivationHours);

            await sp.GetRequiredService<IEmailService>()
                .SendAsync(user.Email, email.Subject, email.HtmlBody, SignupEmails.SenderName, cancellationToken: cancellationToken);

            if (environment.IsDevelopment())
            {
                // Local runs usually mock e-mail; the link is the quickest way in. The password is never logged.
                logger.LogInformation("Development only — set-password link for {TenantKey}: {ActivationLink}", request.Tenant.Key, link);
            }

            return Result<TenantCredentialsDelivery>.Success(
                mail.UseMock ? TenantCredentialsDelivery.WrittenToMockFolder : TenantCredentialsDelivery.Sent);
        });
    }

    private static async Task<List<ApplicationUser>> BackOfficeAdministratorsAsync(UserManager<ApplicationUser> userManager) =>
        (await userManager.GetUsersInRoleAsync(Roles.Administrator)).ToList();

    private bool IsSuspended(ApplicationUser user) =>
        user.LockoutEnd is { } end && end > timeProvider.GetUtcNow();

    private TenantAdministratorDto ToDto(ApplicationUser user) => new()
    {
        UserId = user.Id.ToString(),
        UserName = user.UserName ?? user.Email ?? user.Id.ToString(),
        Email = user.Email,
        PhoneNumber = user.PhoneNumber,
        Status = IsSuspended(user)
            ? TenantAdministratorStatuses.Suspended
            : user.PasswordHash is null
                ? TenantAdministratorStatuses.PendingActivation
                : TenantAdministratorStatuses.Active,
        CreatedAt = null
    };

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
        }
    }

    private async Task<T> RunAsync<T>(TenantInfo tenant, Func<IServiceProvider, Task<T>> work)
    {
        using var tenantScope = tenantContext.BeginScope(tenant);
        await using var scope = scopeFactory.CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }
}
