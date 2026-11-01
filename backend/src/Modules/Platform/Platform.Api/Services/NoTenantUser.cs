using NWFM.Shared.Abstractions;

namespace Platform.Api.Services;

/// <summary>
/// The control plane serves no tenant users. Shared pipeline pieces still ask for
/// <see cref="ICurrentUser"/>; they get an anonymous one, so a tenant-only <c>[Authorize]</c>
/// request can never pass here.
/// </summary>
public sealed class NoTenantUser : ICurrentUser
{
    public string? Id => null;

    public string? UserName => null;

    public string? Email => null;

    public IReadOnlyList<string> Roles => [];

    public IReadOnlyList<string> Permissions => [];

    public Guid? TeamId => null;

    public bool IsSupportSession => false;

    public Guid? ActorId => null;

    public string? SupportActorName => null;

    public string? SupportScope => null;

    public Guid? SupportSessionId => null;

    public bool IsInRole(string role) => false;

    public bool HasPermission(string permissionCode) => false;
}
