using Workflow.Domain.Entities;

namespace Workflow.Application.Abstractions;

/// <summary>Role assignments use live account membership plus active participation in this tenant.</summary>
public interface IWorkflowRoleAssignments
{
    Task<bool> CanAccessAsync(WorkItem item, Guid userId, CancellationToken ct);
    Task<IReadOnlyList<WorkItem>> AvailableAsync(Guid organizationId, Guid userId, bool overdue, CancellationToken ct);
    Task<IReadOnlyList<WorkItemCandidate>> CreateCandidatesAsync(Guid organizationId, Guid workItemId,
        IReadOnlyList<Guid> roleIds, DateTime now, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IReadOnlyCollection<Guid> workItemIds, CancellationToken ct);
}
