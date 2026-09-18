namespace Workflow.Domain.Repositories;

using Workflow.Domain.Entities;

public interface IWorkflowBindingAssignmentMappingRepository
{
    Task<WorkflowBindingAssignmentMapping?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<WorkflowBindingAssignmentMapping>> GetByBindingIdAsync(Guid bindingId, Guid organizationId, CancellationToken cancellationToken = default);
    Task<bool> MappingExistsAsync(Guid bindingId, string assignmentKey, Guid organizationId, CancellationToken cancellationToken = default);
    Task<List<string>> GetMappedKeysAsync(Guid bindingId, Guid organizationId, CancellationToken cancellationToken = default);
    Task<WorkflowBindingAssignmentMapping?> GetByAssignmentKeyAsync(Guid bindingId, Guid organizationId, string assignmentKey, CancellationToken cancellationToken = default);
    Task AddAsync(WorkflowBindingAssignmentMapping mapping, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
