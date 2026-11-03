using System.Security.Claims;
using Auth.Domain.Constants;
using NWFM.Shared.Abstractions;
using NWFM.Shared.MultiTenancy;

namespace NWFM.Api.Services;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User is { Identity.IsAuthenticated: true } user
        ? user : null;

    public string? Id => User?.FindFirstValue(ClaimTypes.NameIdentifier);
    public string? UserName => User?.Identity?.Name;
    public string? Email => User?.FindFirstValue(ClaimTypes.Email);

    public IReadOnlyList<string> Roles =>
        User?.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList()
        ?? (IReadOnlyList<string>)[];

    public IReadOnlyList<string> Permissions =>
        User?.Claims.Where(c => c.Type == PermissionClaimTypes.Permission).Select(c => c.Value).ToList()
        ?? (IReadOnlyList<string>)[];

    public Guid? TeamId
    {
        get
        {
            var claim = User?.FindFirstValue(AppClaimTypes.TeamId);
            return claim is not null && Guid.TryParse(claim, out var id) ? id : null;
        }
    }

    public bool IsSupportSession => SupportSessionId is not null;

    public Guid? ActorId => Guid.TryParse(User?.FindFirstValue(TenantClaimTypes.Actor), out var id) ? id : null;

    public string? SupportActorName => User?.FindFirstValue(TenantClaimTypes.ActorName);

    public string? SupportScope => User?.FindFirstValue(TenantClaimTypes.SupportScope);

    public Guid? SupportSessionId => Guid.TryParse(User?.FindFirstValue(TenantClaimTypes.SupportSessionId), out var id) ? id : null;

    public bool IsInRole(string role) => User?.IsInRole(role) ?? false;

    public bool HasPermission(string permissionCode) =>
        User?.HasClaim(PermissionClaimTypes.Permission, permissionCode) ?? false;
}
