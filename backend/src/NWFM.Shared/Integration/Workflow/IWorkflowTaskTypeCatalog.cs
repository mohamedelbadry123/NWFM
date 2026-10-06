namespace NWFM.Shared.Integration.Workflow;

/// <summary>Read-only task classifications for workflow definitions, owned by Tasks.</summary>
public interface IWorkflowTaskTypeCatalog
{
    Task<IReadOnlyList<WorkflowLookupItem>> ListAsync(CancellationToken ct);
    Task<bool> IsActiveAsync(Guid id, CancellationToken ct);
}
