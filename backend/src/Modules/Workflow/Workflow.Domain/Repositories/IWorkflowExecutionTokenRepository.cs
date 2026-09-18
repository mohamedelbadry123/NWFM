namespace Workflow.Domain.Repositories;

using Workflow.Domain.Entities;

public interface IWorkflowExecutionTokenRepository
{
    Task<WorkflowExecutionToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WorkflowExecutionToken?> GetByBranchAsync(Guid workflowInstanceId, string branchKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowExecutionToken>> GetByInstanceIdAsync(Guid workflowInstanceId, CancellationToken cancellationToken = default);
    Task AddAsync(WorkflowExecutionToken token, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
