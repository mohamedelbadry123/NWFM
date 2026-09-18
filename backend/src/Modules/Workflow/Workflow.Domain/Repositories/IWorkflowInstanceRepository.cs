namespace Workflow.Domain.Repositories;

using Workflow.Domain.Entities;

public interface IWorkflowInstanceRepository
{
    Task<WorkflowInstance?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WorkflowInstance?> GetByIdForSuperAdminAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsByIdempotencyKeyAsync(Guid organizationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<WorkflowInstance?> GetByIdempotencyKeyAsync(Guid organizationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<WorkflowInstance?> GetByBusinessEntityAsync(Guid organizationId, string moduleKey, string entityType, string entityId, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<WorkflowInstance> Items, int TotalCount)> GetPagedByOrgAsync(Guid organizationId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<WorkflowInstance> Items, int TotalCount)> GetPagedAllAsync(int pageNumber, int pageSize, Guid? organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowInstance>> GetByParentAsync(Guid parentInstanceId, CancellationToken cancellationToken = default);
    Task AddAsync(WorkflowInstance instance, CancellationToken cancellationToken = default);
}
