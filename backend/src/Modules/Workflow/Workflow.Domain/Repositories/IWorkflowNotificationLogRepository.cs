namespace Workflow.Domain.Repositories;

using Workflow.Domain.Entities;

public interface IWorkflowNotificationLogRepository
{
    Task AddAsync(WorkflowNotificationLog log, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a paged list of notification logs for the given organization, newest first.
    /// Optionally filter by recipient user id (exact match inside RecipientsJson array).
    /// </summary>
    Task<(IReadOnlyList<WorkflowNotificationLog> Items, int TotalCount)> GetPagedByOrgAsync(
        Guid organizationId,
        int pageNumber,
        int pageSize,
        Guid? recipientUserId = null,
        CancellationToken cancellationToken = default);
}
