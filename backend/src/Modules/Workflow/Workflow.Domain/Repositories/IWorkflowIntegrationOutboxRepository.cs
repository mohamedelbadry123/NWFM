namespace Workflow.Domain.Repositories;

using Workflow.Domain.Entities;

public interface IWorkflowIntegrationOutboxRepository
{
    Task<WorkflowIntegrationOutbox?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WorkflowIntegrationOutbox?> GetByMessageIdAsync(string messageId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowIntegrationOutbox>> GetPendingAsync(int take, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<WorkflowIntegrationOutbox> Items, int TotalCount)> GetFailedOutboxPagedAsync(
        Guid organizationId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task AddAsync(WorkflowIntegrationOutbox message, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
