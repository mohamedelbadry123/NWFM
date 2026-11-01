using Auth.Domain.Constants;
using Auth.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Platform.Infrastructure.Provisioning;

/// <summary>
/// The first back-office account of a new tenant. Created with no password; the credentials e-mail
/// (<c>ITenantAdministratorDirectory.SendCredentialsAsync</c>)
/// then gives it a temporary one, changed at first sign-in, and a single-use set-password link.
/// </summary>
internal sealed class TenantAdministratorProvisioner(IServiceScopeFactory scopeFactory, ITenantContext tenantContext)
{
    /// <summary>Creates the administrator, or returns the existing one on a re-run. The e-mail is the user name.</summary>
    public Task<string> EnsureAdministratorAsync(TenantInfo tenant, string email, string? phone) =>
        RunAsync(tenant, async userManager =>
        {
            var existing = await userManager.FindByEmailAsync(email);
            var user = existing ?? new ApplicationUser
            {
                UserName = email,
                Email = email,
                PhoneNumber = phone,
                EmailConfirmed = true
            };

            if (existing is null)
            {
                Ensure(await userManager.CreateAsync(user));
            }

            if (!await userManager.IsInRoleAsync(user, Roles.Administrator))
            {
                Ensure(await userManager.AddToRoleAsync(user, Roles.Administrator));
            }

            return user.Id.ToString();
        });

    private async Task<T> RunAsync<T>(TenantInfo tenant, Func<UserManager<ApplicationUser>, Task<T>> work)
    {
        using var tenantScope = tenantContext.BeginScope(tenant);
        await using var scope = scopeFactory.CreateAsyncScope();

        return await work(scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
    }

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
        }
    }
}
