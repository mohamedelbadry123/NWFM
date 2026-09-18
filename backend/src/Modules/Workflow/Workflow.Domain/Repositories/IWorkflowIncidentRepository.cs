namespace Workflow.Domain.Repositories;

using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

public interface IWorkflowIncidentRepository
{
    Task<WorkflowIncident?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowIncident>> GetOpenByInstanceIdAsync(Guid workflowInstanceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowIncident>> GetByInstanceIdAsync(Guid workflowInstanceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowIncident>> GetByStatusAsync(Guid organizationId, WorkflowIncidentStatus status, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<WorkflowIncident> Items, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        Guid? organizationId = null,
        WorkflowIncidentStatus? status = null,
        CancellationToken cancellationToken = default);
    Task AddAsync(WorkflowIncident incident, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
