namespace NWFM.Shared.Integration.Workflow;

/// <summary>Read-only access to existing identity roles. Workflow assignments never grant account roles.</summary>
public interface IWorkflowRoleDirectory
{
    Task<IReadOnlyList<WorkflowLookupItem>> ListAsync(CancellationToken ct);
    Task<IReadOnlyList<Guid>> GetRoleIdsForUserAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<Guid>> GetUserIdsForRolesAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken ct);
    Task<NWFM.Shared.Organization.OrgScopeSet?> GetScopeForUserAsync(Guid userId, CancellationToken ct);
}
