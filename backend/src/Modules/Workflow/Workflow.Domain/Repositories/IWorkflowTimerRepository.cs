namespace Workflow.Domain.Repositories;

using Workflow.Domain.Entities;

public interface IWorkflowTimerRepository
{
    Task<WorkflowTimer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowTimer>> GetPendingDueAsync(DateTime asOfUtc, int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowTimer>> GetByInstanceIdAsync(Guid workflowInstanceId, CancellationToken cancellationToken = default);
    Task AddAsync(WorkflowTimer timer, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
