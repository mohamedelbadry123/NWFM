using NWFM.Shared.Organization;

namespace NWFM.Shared.Integration.Workflow;

/// <summary>Re-resolves a persisted actor for background work; absence is never system access.</summary>
public interface IWorkflowActorAccess
{
    Task<WorkflowActorAccess?> GetAsync(Guid actorId, CancellationToken ct);
}

public sealed record WorkflowActorAccess(IReadOnlyList<string> Permissions, OrgScopeSet Scope, OrgHierarchy Hierarchy, bool IsAdministrator = false)
{
    public bool HasPermission(string permission) => IsAdministrator || Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
}
