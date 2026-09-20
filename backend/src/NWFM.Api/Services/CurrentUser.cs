using System.Security.Claims;
using Auth.Domain.Constants;
using NWFM.Shared.Abstractions;

namespace NWFM.Api.Services;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public string? Id => User?.FindFirstValue(ClaimTypes.NameIdentifier);
    public string? UserName => User?.Identity?.Name;
    public string? Email => User?.FindFirstValue(ClaimTypes.Email);

    public IReadOnlyList<string> Roles =>
        User?.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList()
        ?? (IReadOnlyList<string>)[];

    public IReadOnlyList<string> Permissions =>
        User?.Claims.Where(c => c.Type == PermissionClaimTypes.Permission).Select(c => c.Value).ToList()
        ?? (IReadOnlyList<string>)[];

    public long? TeamId
    {
        get
        {
            var claim = User?.FindFirstValue(AppClaimTypes.TeamId);
            return claim is not null && long.TryParse(claim, out var id) ? id : null;
        }
    }

    public bool IsInRole(string role) => User?.IsInRole(role) ?? false;

    public bool HasPermission(string permissionCode) =>
        User?.HasClaim(PermissionClaimTypes.Permission, permissionCode) ?? false;
}
