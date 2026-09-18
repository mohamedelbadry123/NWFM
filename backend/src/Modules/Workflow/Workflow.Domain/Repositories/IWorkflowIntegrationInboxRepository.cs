namespace Workflow.Domain.Repositories;

using Workflow.Domain.Entities;

public interface IWorkflowIntegrationInboxRepository
{
    Task<WorkflowIntegrationInbox?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WorkflowIntegrationInbox?> GetByMessageIdAsync(string messageId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowIntegrationInbox>> GetPendingAsync(int take, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<WorkflowIntegrationInbox> Items, int TotalCount)> GetDeadLettersPagedAsync(
        Guid organizationId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task AddAsync(WorkflowIntegrationInbox message, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
