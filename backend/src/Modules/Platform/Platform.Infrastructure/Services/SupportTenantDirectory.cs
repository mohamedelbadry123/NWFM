using Auth.Domain.Constants;
using Auth.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Common.Interfaces;

namespace Platform.Infrastructure.Services;

/// <summary>
/// Opens a nested tenant scope so Identity and the tenant audit table are reachable from a
/// platform request, which itself is never bound to a tenant.
/// </summary>
internal sealed class SupportTenantDirectory(
    IServiceScopeFactory scopeFactory,
    ITenantContext tenantContext)
    : ISupportTenantDirectory
{
    public Task<Result<SupportImpersonationCandidateDto>> ResolveUserAsync(
        TenantInfo tenant,
        string? impersonatedUserId,
        CancellationToken cancellationToken) =>
        RunAsync(tenant, async sp =>
        {
            var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser? user;

            if (!string.IsNullOrWhiteSpace(impersonatedUserId))
            {
                user = await userManager.FindByIdAsync(impersonatedUserId);
            }
            else
            {
                var administrators = await userManager.GetUsersInRoleAsync(Roles.Administrator);
                user = administrators.FirstOrDefault();
            }

            if (user is null)
            {
                return PlatformResults.Fail<SupportImpersonationCandidateDto>(
                    "The account to act as was not found.",
                    ApiErrorCodes.NotFound,
                    httpStatusCode: 404);
            }

            if (await userManager.IsLockedOutAsync(user))
            {
                return PlatformResults.Fail<SupportImpersonationCandidateDto>(
                    "The account to act as is locked or inactive.",
                    ApiErrorCodes.AuthenticationFailed,
                    httpStatusCode: 403);
            }

            var roles = await userManager.GetRolesAsync(user);
            return Result<SupportImpersonationCandidateDto>.Success(new SupportImpersonationCandidateDto
            {
                UserId = user.Id.ToString(),
                UserName = user.UserName ?? user.Email ?? user.Id.ToString(),
                Email = user.Email,
                Roles = roles.ToList()
            });
        });

    public Task<Result<IReadOnlyList<SupportImpersonationCandidateDto>>> ListCandidatesAsync(
        TenantInfo tenant,
        string? searchTerm,
        CancellationToken cancellationToken) =>
        RunAsync(tenant, async sp =>
        {
            var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            IQueryable<ApplicationUser> query = userManager.Users;

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim();
                query = query.Where(u =>
                    (u.UserName != null && u.UserName.Contains(term))
                    || (u.Email != null && u.Email.Contains(term)));
            }

            var users = await query.OrderBy(u => u.UserName).Take(50).ToListAsync(cancellationToken);
            var items = new List<SupportImpersonationCandidateDto>(users.Count);

            foreach (var user in users)
            {
                var roles = await userManager.GetRolesAsync(user);
                items.Add(new SupportImpersonationCandidateDto
                {
                    UserId = user.Id.ToString(),
                    UserName = user.UserName ?? user.Email ?? user.Id.ToString(),
                    Email = user.Email,
                    Roles = roles.ToList()
                });
            }

            return Result<IReadOnlyList<SupportImpersonationCandidateDto>>.Success(items);
        });

    private async Task<T> RunAsync<T>(TenantInfo tenant, Func<IServiceProvider, Task<T>> work)
    {
        using var tenantScope = tenantContext.BeginScope(tenant);
        await using var scope = scopeFactory.CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }
}
