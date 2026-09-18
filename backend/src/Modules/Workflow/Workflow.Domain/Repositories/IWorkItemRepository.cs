namespace Workflow.Domain.Repositories;

using Workflow.Domain.Entities;

public interface IWorkItemRepository
{
    Task<WorkItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkItem>> GetPendingByGroupAsync(Guid organizationId, Guid assignmentGroupId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkItem>> GetPendingForGroupIdsAsync(Guid organizationId, IReadOnlyCollection<Guid> groupIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkItem>> GetPendingForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkItem>> GetOverdueForOrganizationAsync(Guid organizationId, DateTime asOfUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkItem>> GetByClaimedUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkItem>> GetOverdueForUserAsync(
        Guid organizationId,
        Guid userId,
        IReadOnlyCollection<Guid> groupIds,
        DateTime asOfUtc,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkItem>> GetByInstanceIdAsync(Guid workflowInstanceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkItem>> GetByInstanceIdForSuperAdminAsync(Guid workflowInstanceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkItem>> GetOverdueForReminderAsync(DateTime asOfUtc, int take, CancellationToken cancellationToken = default);
    Task<int> CountOpenClaimedByUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default);
    Task<DateTime?> GetLastClaimedAtByUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(Guid GroupId, string GroupName, int PendingCount, int OverdueCount, int ClaimedCount)>> GetWorkloadByGroupAsync(
        Guid organizationId, DateTime asOfUtc, CancellationToken cancellationToken = default);
    Task<(int Open, int Overdue, int CompletedToday)> GetWorkloadTotalsAsync(
        Guid organizationId, DateTime asOfUtc, DateTime todayStartUtc, CancellationToken cancellationToken = default);
    Task AddAsync(WorkItem workItem, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claims a Pending work item. Returns false on not-found, not-pending, or concurrency conflict.
    /// </summary>
    Task<bool> TryClaimAsync(
        Guid workItemId,
        Guid organizationId,
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken = default);
}
