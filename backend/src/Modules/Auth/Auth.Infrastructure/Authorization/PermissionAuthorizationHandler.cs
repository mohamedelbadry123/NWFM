using System.Security.Claims;
using Auth.Application.Common.Interfaces;
using NWFM.Shared.Constants;
using Auth.Domain.Constants;
using Microsoft.AspNetCore.Authorization;

namespace Auth.Infrastructure.Authorization;

public sealed class PermissionAuthorizationHandler(IPermissionResolver permissionResolver) : IAuthorizationHandler
{
    public async Task HandleAsync(AuthorizationHandlerContext context)
    {
        List<IAuthorizationRequirement> pending = context.PendingRequirements.ToList();
        if (!pending.Any(r => r is PermissionRequirement or AnyPermissionRequirement))
            return;

        if (context.User.IsInRole(Roles.Administrator))
        {
            foreach (IAuthorizationRequirement req in pending.Where(r => r is PermissionRequirement or AnyPermissionRequirement))
                context.Succeed(req);
            return;
        }

        IReadOnlyList<string>? resolvedPermissions = await ResolvePermissionsAsync(context.User);
        HashSet<string>? resolvedSet = resolvedPermissions is { Count: > 0 }
            ? new HashSet<string>(resolvedPermissions, StringComparer.OrdinalIgnoreCase)
            : null;

        foreach (IAuthorizationRequirement req in pending)
        {
            switch (req)
            {
                case PermissionRequirement pr:
                    if (HasPermission(context.User, pr.PermissionCode, resolvedSet))
                        context.Succeed(req);
                    break;
                case AnyPermissionRequirement apr:
                    if (apr.PermissionCodes.Any(code => HasPermission(context.User, code, resolvedSet)))
                        context.Succeed(req);
                    break;
            }
        }
    }

    private async Task<IReadOnlyList<string>?> ResolvePermissionsAsync(ClaimsPrincipal user)
    {
        if (user.HasClaim(c => c.Type == PermissionClaimTypes.Permission))
            return null;
        string? userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return [];
        return await permissionResolver.GetPermissionCodesForUserAsync(userId);
    }

    private static bool HasPermission(ClaimsPrincipal user, string code, HashSet<string>? resolved)
    {
        if (user.HasClaim(PermissionClaimTypes.Permission, code)) return true;
        return resolved?.Contains(code) ?? false;
    }
}
