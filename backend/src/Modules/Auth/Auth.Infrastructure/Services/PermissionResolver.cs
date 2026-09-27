using Auth.Application.Common.Interfaces;
using Auth.Infrastructure.Identity;
using Auth.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Caching;
using NWFM.Shared.Constants;

namespace Auth.Infrastructure.Services;

public sealed class PermissionResolver(
    AuthDbContext context,
    UserManager<ApplicationUser> userManager,
    ICacheService cache) : IPermissionResolver
{
    private const int CacheMinutes = 15;
    private const int LocalCacheMinutes = 2;

    private static readonly CacheEntryOptions EntryOptions =
        CacheEntryOptions.FromMinutes(CacheMinutes, LocalCacheMinutes);

    public async Task<IReadOnlyList<string>> GetPermissionCodesForUserAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        ApplicationUser? user = await userManager.FindByIdAsync(userId);
        if (user is null) return [];

        return await cache.GetOrCreateAsync(
            CacheKeys.Auth.Permissions.ForUser(userId),
            token => LoadForUserAsync(user, token),
            EntryOptions,
            cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetPermissionCodesForRoleAsync(
        string roleId, CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(
            CacheKeys.Auth.Permissions.ForRole(roleId),
            token => LoadForRoleAsync(roleId, token),
            EntryOptions,
            cancellationToken);
    }

    public Task InvalidateUserAsync(string userId, CancellationToken cancellationToken = default) =>
        cache.RemoveAsync(CacheKeys.Auth.Permissions.ForUser(userId), cancellationToken).AsTask();

    public async Task InvalidateRoleAsync(string roleId, CancellationToken cancellationToken = default)
    {
        List<string> userIds = await context.UserRoles
            .AsNoTracking()
            .Where(x => x.RoleId.ToString() == roleId)
            .Select(x => x.UserId.ToString())
            .ToListAsync(cancellationToken);

        var keys = new List<string>(userIds.Count + 1) { CacheKeys.Auth.Permissions.ForRole(roleId) };
        keys.AddRange(userIds.Select(CacheKeys.Auth.Permissions.ForUser));
        await cache.RemoveAsync(keys, cancellationToken);
    }

    private async ValueTask<string[]> LoadForUserAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        IList<string> roleNames = await userManager.GetRolesAsync(user);
        if (roleNames.Count == 0) return [];

        List<string> roleIds = await context.Roles
            .AsNoTracking()
            .Where(role => role.Name != null && roleNames.Contains(role.Name))
            .Select(role => role.Id.ToString())
            .ToListAsync(cancellationToken);

        if (roleIds.Count == 0) return [];

        return await context.RolePermissions
            .AsNoTracking()
            .Where(x => roleIds.Contains(x.RoleId))
            .Where(x => x.Permission.IsActive)
            .Select(x => x.Permission.Code)
            .Distinct()
            .OrderBy(x => x)
            .ToArrayAsync(cancellationToken);
    }

    private async ValueTask<string[]> LoadForRoleAsync(string roleId, CancellationToken cancellationToken) =>
        await context.RolePermissions
            .AsNoTracking()
            .Where(x => x.RoleId == roleId)
            .Where(x => x.Permission.IsActive)
            .Select(x => x.Permission.Code)
            .Distinct()
            .OrderBy(x => x)
            .ToArrayAsync(cancellationToken);
}
