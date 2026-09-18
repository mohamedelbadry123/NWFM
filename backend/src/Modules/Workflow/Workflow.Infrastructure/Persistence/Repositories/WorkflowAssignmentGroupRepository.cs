namespace Workflow.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

internal sealed class WorkflowAssignmentGroupRepository : IWorkflowAssignmentGroupRepository
{
    private readonly WorkflowDbContext _db;

    public WorkflowAssignmentGroupRepository(WorkflowDbContext db) => _db = db;

    public Task<WorkflowAssignmentGroup?> GetByIdAsync(
        Guid id, Guid organizationId, CancellationToken cancellationToken = default)
        // IgnoreQueryFilters: background workers and SuperAdmin resolve a group for a known org.
        => _db.AssignmentGroups
            
            .FirstOrDefaultAsync(g => g.Id == id && g.OrganizationId == organizationId,
                cancellationToken);

    public Task<WorkflowAssignmentGroup?> GetByIdWithMembersAsync(
        Guid id, Guid organizationId, CancellationToken cancellationToken = default)
        => _db.AssignmentGroups
            .Include(g => g.Members)
                .ThenInclude(m => m.Participant)
            .FirstOrDefaultAsync(g => g.Id == id && g.OrganizationId == organizationId,
                cancellationToken);

    public Task<bool> CodeExistsAsync(
        string code,
        Guid organizationId,
        Guid? excludingGroupId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.AssignmentGroups
            .Where(g => g.Code == code && g.OrganizationId == organizationId);
        if (excludingGroupId.HasValue)
            query = query.Where(g => g.Id != excludingGroupId.Value);
        return query.AnyAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<WorkflowAssignmentGroup> Items, int TotalCount)> GetPagedAsync(
        Guid organizationId,
        int pageNumber,
        int pageSize,
        string? searchTerm,
        CancellationToken cancellationToken = default)
    {
        var query = _db.AssignmentGroups
            .Include(g => g.Members)
            .Where(g => g.OrganizationId == organizationId);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLower();
            query = query.Where(g =>
                g.Name.ToLower().Contains(term) ||
                g.Code.ToLower().Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(g => g.Name)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<IReadOnlyList<WorkflowAssignmentGroup>> GetActiveGroupsForOrgAsync(
        Guid organizationId, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters: SuperAdmin cross-tenant read — lists active groups for a specific org
        // to populate the binding wizard assignment-key mapping dropdowns.
        return await _db.AssignmentGroups
            
            .Where(g => g.OrganizationId == organizationId && g.IsActive)
            .OrderBy(g => g.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<WorkflowAssignmentGroup?> GetByCodeAsync(
        string code, Guid organizationId, CancellationToken cancellationToken = default)
        // IgnoreQueryFilters: runtime / SuperAdmin resolve a group code inside one organization.
        => _db.AssignmentGroups
            
            .FirstOrDefaultAsync(
                g => g.OrganizationId == organizationId
                     && g.IsActive
                     && g.Code == code,
                cancellationToken);

    public async Task AddAsync(WorkflowAssignmentGroup group, CancellationToken cancellationToken = default)
        => await _db.AssignmentGroups.AddAsync(group, cancellationToken);

    public async Task AddMemberAsync(WorkflowGroupMember member, CancellationToken cancellationToken = default)
        => await _db.GroupMembers.AddAsync(member, cancellationToken);

    public Task<WorkflowGroupMember?> GetMemberAsync(
        Guid groupId, Guid participantId, CancellationToken cancellationToken = default)
        => _db.GroupMembers
            .FirstOrDefaultAsync(m => m.AssignmentGroupId == groupId && m.ParticipantId == participantId,
                cancellationToken);

    public Task RemoveMemberAsync(WorkflowGroupMember member, CancellationToken cancellationToken = default)
    {
        _db.GroupMembers.Remove(member);
        return Task.CompletedTask;
    }

    public Task<bool> IsUserMemberOfGroupAsync(
        Guid groupId, Guid userId, Guid organizationId, CancellationToken cancellationToken = default)
    {
        // Inbox and Claim are gated by active group membership. CanClaim is a display/routing
        // preference — a hidden false default must not hide or block work for people in the group.
        var now = DateTime.UtcNow;
        return (from gm in _db.GroupMembers
                join p in _db.Participants on gm.ParticipantId equals p.Id
                join g in _db.AssignmentGroups on gm.AssignmentGroupId equals g.Id
                where gm.AssignmentGroupId == groupId
                   && p.UserId == userId
                   && p.OrganizationId == organizationId
                   && p.IsActive
                   && g.IsActive
                   && (gm.ValidFrom == null || gm.ValidFrom <= now)
                   && (gm.ValidTo == null || gm.ValidTo >= now)
                select gm.Id)
            .AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetGroupIdsForUserAsync(
        Guid userId, Guid organizationId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await (from gm in _db.GroupMembers
                      join p in _db.Participants on gm.ParticipantId equals p.Id
                      join g in _db.AssignmentGroups on gm.AssignmentGroupId equals g.Id
                      where p.UserId == userId
                         && p.OrganizationId == organizationId
                         && p.IsActive
                         && g.IsActive
                         && (gm.ValidFrom == null || gm.ValidFrom <= now)
                         && (gm.ValidTo == null || gm.ValidTo >= now)
                      select g.Id)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(
        Guid organizationId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        return await _db.AssignmentGroups
            .Where(g => g.OrganizationId == organizationId && ids.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Name, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}
