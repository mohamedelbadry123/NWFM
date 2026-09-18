namespace Workflow.Domain.Repositories;

using Workflow.Domain.Entities;

public interface IWorkflowParticipantRepository
{
    Task<WorkflowParticipant?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken cancellationToken = default);
    Task<WorkflowParticipant?> GetByUserIdAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken = default);
    Task<bool> ExistsByUserIdAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken = default);
    Task<bool> ExistsActiveForUserAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowParticipant>> GetByUserIdsAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> userIds,
        bool ignoreTenantFilters = false,
        CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<WorkflowParticipant> Items, int TotalCount)> GetPagedAsync(
        Guid organizationId,
        int pageNumber,
        int pageSize,
        string? searchTerm,
        CancellationToken cancellationToken = default);
    Task AddAsync(WorkflowParticipant participant, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
