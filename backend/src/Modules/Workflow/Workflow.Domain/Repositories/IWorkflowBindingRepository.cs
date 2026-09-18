namespace Workflow.Domain.Repositories;

using Workflow.Domain.Entities;

public interface IWorkflowBindingRepository
{
    Task<WorkflowBinding?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> BindingExistsAsync(Guid definitionId, Guid organizationId, string moduleKey, string entityType, string triggerEvent, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<WorkflowBinding> Items, int TotalCount)> GetPagedByDefinitionAsync(
        Guid definitionId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<WorkflowBinding> Items, int TotalCount)> GetPagedByOrganizationAsync(
        Guid organizationId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<WorkflowBinding?> GetByIdForSuperAdminAsync(Guid id, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<WorkflowBinding> Items, int TotalCount)> GetPagedAllAsync(
        int pageNumber, int pageSize,
        Guid? definitionId, Guid? organizationId,
        CancellationToken cancellationToken = default);
    Task AddAsync(WorkflowBinding binding, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
