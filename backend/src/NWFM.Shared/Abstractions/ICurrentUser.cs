namespace NWFM.Shared.Abstractions;

public interface ICurrentUser
{
    string? Id { get; }
    string? UserName { get; }
    string? Email { get; }
    IReadOnlyList<string> Roles { get; }
    IReadOnlyList<string> Permissions { get; }
    Guid? TeamId { get; }

    /// <summary>True when platform support is acting inside the tenant under a granted session.</summary>
    bool IsSupportSession => SupportSessionId is not null;

    /// <summary>The platform user id behind a support session (<c>act</c>).</summary>
    Guid? ActorId => null;

    /// <summary>The platform staff member behind a support session (<c>act_name</c>).</summary>
    string? SupportActorName => null;

    /// <summary><c>readonly</c> or <c>full</c> on a support session; null otherwise.</summary>
    string? SupportScope => null;

    Guid? SupportSessionId => null;
    bool IsInRole(string role);
    bool HasPermission(string permissionCode);
}
