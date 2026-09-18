namespace Workflow.Domain.Repositories;

using Workflow.Domain.Entities;

public interface IWorkflowAssignmentGroupRepository
{
    Task<WorkflowAssignmentGroup?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken cancellationToken = default);
    Task<WorkflowAssignmentGroup?> GetByIdWithMembersAsync(Guid id, Guid organizationId, CancellationToken cancellationToken = default);
    Task<bool> CodeExistsAsync(
        string code,
        Guid organizationId,
        Guid? excludingGroupId = null,
        CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<WorkflowAssignmentGroup> Items, int TotalCount)> GetPagedAsync(
        Guid organizationId,
        int pageNumber,
        int pageSize,
        string? searchTerm,
        CancellationToken cancellationToken = default);
    Task AddAsync(WorkflowAssignmentGroup group, CancellationToken cancellationToken = default);
    Task AddMemberAsync(WorkflowGroupMember member, CancellationToken cancellationToken = default);
    Task<WorkflowGroupMember?> GetMemberAsync(Guid groupId, Guid participantId, CancellationToken cancellationToken = default);
    Task RemoveMemberAsync(WorkflowGroupMember member, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowAssignmentGroup>> GetActiveGroupsForOrgAsync(
        Guid organizationId, CancellationToken cancellationToken = default);
    Task<WorkflowAssignmentGroup?> GetByCodeAsync(
        string code, Guid organizationId, CancellationToken cancellationToken = default);
    Task<bool> IsUserMemberOfGroupAsync(Guid groupId, Guid userId, Guid organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> GetGroupIdsForUserAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(
        Guid organizationId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
