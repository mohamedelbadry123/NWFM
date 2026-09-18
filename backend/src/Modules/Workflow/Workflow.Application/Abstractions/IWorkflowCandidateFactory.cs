namespace Workflow.Application.Abstractions;

using Workflow.Domain.Entities;

/// <summary>
/// Builds WorkItemCandidate snapshots (group + claimable users) with optional strategy-based primary.
/// </summary>
public interface IWorkflowCandidateFactory
{
    Task<IReadOnlyList<WorkItemCandidate>> CreateCandidatesAsync(
        Guid organizationId,
        Guid workItemId,
        Guid assignmentGroupId,
        DateTime createdAt,
        CancellationToken cancellationToken = default);
}
