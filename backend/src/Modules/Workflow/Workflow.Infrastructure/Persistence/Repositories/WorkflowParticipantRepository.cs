namespace Workflow.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

internal sealed class WorkflowParticipantRepository : IWorkflowParticipantRepository
{
    private readonly WorkflowDbContext _db;

    public WorkflowParticipantRepository(WorkflowDbContext db) => _db = db;

    public Task<WorkflowParticipant?> GetByIdAsync(
        Guid id, Guid organizationId, CancellationToken cancellationToken = default)
        => _db.Participants
            .FirstOrDefaultAsync(p => p.Id == id && p.OrganizationId == organizationId,
                cancellationToken);

    public Task<WorkflowParticipant?> GetByUserIdAsync(
        Guid userId, Guid organizationId, CancellationToken cancellationToken = default)
        => _db.Participants
            .FirstOrDefaultAsync(p => p.UserId == userId && p.OrganizationId == organizationId,
                cancellationToken);

    public Task<bool> ExistsByUserIdAsync(
        Guid userId, Guid organizationId, CancellationToken cancellationToken = default)
        => _db.Participants
            .AnyAsync(p => p.UserId == userId && p.OrganizationId == organizationId,
                cancellationToken);

    public Task<bool> ExistsActiveForUserAsync(
        Guid userId, Guid organizationId, CancellationToken cancellationToken = default)
        => _db.Participants
            .AnyAsync(p => p.UserId == userId && p.OrganizationId == organizationId && p.IsActive,
                cancellationToken);

    public async Task<IReadOnlyList<WorkflowParticipant>> GetByUserIdsAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> userIds,
        bool ignoreTenantFilters = false,
        CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0) return [];

        IQueryable<WorkflowParticipant> query = _db.Participants;

        return await query
            .Where(p => p.OrganizationId == organizationId && userIds.Contains(p.UserId))
            .ToListAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<WorkflowParticipant> Items, int TotalCount)> GetPagedAsync(
        Guid organizationId,
        int pageNumber,
        int pageSize,
        string? searchTerm,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Participants
            .Where(p => p.OrganizationId == organizationId);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLower();
            query = query.Where(p =>
                p.DisplayName.ToLower().Contains(term) ||
                p.Email.ToLower().Contains(term) ||
                (p.EmployeeNumber != null && p.EmployeeNumber.ToLower().Contains(term)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(p => p.DisplayName)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task AddAsync(WorkflowParticipant participant, CancellationToken cancellationToken = default)
        => await _db.Participants.AddAsync(participant, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}
