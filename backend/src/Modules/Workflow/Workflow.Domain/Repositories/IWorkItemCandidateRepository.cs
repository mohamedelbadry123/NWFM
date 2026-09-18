namespace Workflow.Domain.Repositories;

using Workflow.Domain.Entities;

public interface IWorkItemCandidateRepository
{
    Task AddRangeAsync(IEnumerable<WorkItemCandidate> candidates, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkItemCandidate>> GetByWorkItemIdAsync(Guid workItemId, CancellationToken cancellationToken = default);
}
