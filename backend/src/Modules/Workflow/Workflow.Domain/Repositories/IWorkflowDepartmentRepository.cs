namespace Workflow.Domain.Repositories;

using Workflow.Domain.Entities;

public interface IWorkflowDepartmentRepository
{
    Task<WorkflowDepartment?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken cancellationToken = default);
    Task<WorkflowDepartment?> GetByIdWithMembersAsync(Guid id, Guid organizationId, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<WorkflowDepartment> Items, int TotalCount)> GetPagedAsync(
        Guid organizationId,
        int pageNumber,
        int pageSize,
        string? searchTerm,
        CancellationToken cancellationToken = default);
    Task AddAsync(WorkflowDepartment department, CancellationToken cancellationToken = default);
    Task AddMemberAsync(WorkflowDepartmentMember member, CancellationToken cancellationToken = default);
    Task<WorkflowDepartmentMember?> GetMemberAsync(Guid departmentId, Guid participantId, CancellationToken cancellationToken = default);
    Task RemoveMemberAsync(WorkflowDepartmentMember member, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
