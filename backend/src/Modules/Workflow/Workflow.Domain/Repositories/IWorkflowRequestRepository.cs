namespace Workflow.Domain.Repositories;

using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

public interface IWorkflowRequestRepository
{
    Task<WorkflowRequest?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken cancellationToken = default);
    Task<WorkflowRequest?> GetByInstanceIdAsync(Guid workflowInstanceId, Guid organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowRequest>> GetByInstanceIdsAsync(
        Guid organizationId, IReadOnlyCollection<Guid> instanceIds, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<WorkflowRequest> Items, int TotalCount)> GetPagedAsync(
        Guid organizationId,
        int pageNumber,
        int pageSize,
        string? search,
        WorkflowInstanceStatus? status,
        string? service,
        string? currentStep,
        Guid? originalGroupId,
        DateTime? fromUtc,
        DateTime? toUtc,
        string? slaStatus,
        string? sortBy,
        DateTime asOfUtc,
        CancellationToken cancellationToken = default);
    Task<int> CountByOrgAndYearAsync(Guid organizationId, int year, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> GetInstanceIdsMissingProjectionAsync(
        Guid organizationId, int take, CancellationToken cancellationToken = default);
    Task<(int Total, int InProgress, int Completed, int Breached)> GetKpisAsync(
        Guid organizationId, DateTime asOfUtc, CancellationToken cancellationToken = default);
    Task AddAsync(WorkflowRequest request, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
