namespace Workflow.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

internal sealed class WorkItemRepository : IWorkItemRepository
{
    private readonly WorkflowDbContext _db;

    public WorkItemRepository(WorkflowDbContext db) => _db = db;

    public Task<WorkItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.WorkItems.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

    public async Task<IReadOnlyList<WorkItem>> GetPendingByGroupAsync(
        Guid organizationId, Guid assignmentGroupId, CancellationToken cancellationToken = default)
    {
        return await _db.WorkItems
            .Where(w => w.OrganizationId == organizationId
                     && w.AssignmentGroupId == assignmentGroupId
                     && w.Status == WorkItemStatus.Pending)
            .OrderBy(w => w.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkItem>> GetPendingForGroupIdsAsync(
        Guid organizationId, IReadOnlyCollection<Guid> groupIds, CancellationToken cancellationToken = default)
    {
        if (groupIds.Count == 0)
            return [];

        return await _db.WorkItems
            .Where(w => w.OrganizationId == organizationId
                     && groupIds.Contains(w.AssignmentGroupId)
                     && w.Status == WorkItemStatus.Pending)
            .OrderBy(w => w.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkItem>> GetPendingForOrganizationAsync(
        Guid organizationId, CancellationToken cancellationToken = default)
    {
        return await _db.WorkItems
            .Where(w => w.OrganizationId == organizationId
                     && w.Status == WorkItemStatus.Pending)
            .OrderBy(w => w.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkItem>> GetOverdueForOrganizationAsync(
        Guid organizationId, DateTime asOfUtc, CancellationToken cancellationToken = default)
    {
        return await _db.WorkItems
            .Where(w => w.OrganizationId == organizationId
                     && w.DueAt != null
                     && w.DueAt < asOfUtc
                     && (w.Status == WorkItemStatus.Pending || w.Status == WorkItemStatus.Claimed))
            .OrderBy(w => w.DueAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkItem>> GetByClaimedUserAsync(
        Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
    {
        return await _db.WorkItems
            .Where(w => w.OrganizationId == organizationId
                     && w.ClaimedByUserId == userId
                     && w.Status == WorkItemStatus.Claimed)
            .OrderBy(w => w.ClaimedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkItem>> GetOverdueForUserAsync(
        Guid organizationId,
        Guid userId,
        IReadOnlyCollection<Guid> groupIds,
        DateTime asOfUtc,
        CancellationToken cancellationToken = default)
    {
        return await _db.WorkItems
            .Where(w => w.OrganizationId == organizationId
                     && w.DueAt != null
                     && w.DueAt < asOfUtc
                     && (w.Status == WorkItemStatus.Pending || w.Status == WorkItemStatus.Claimed)
                     && ((w.Status == WorkItemStatus.Pending && groupIds.Contains(w.AssignmentGroupId))
                         || (w.Status == WorkItemStatus.Claimed && w.ClaimedByUserId == userId)))
            .OrderBy(w => w.DueAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkItem>> GetByInstanceIdAsync(
        Guid workflowInstanceId, CancellationToken cancellationToken = default)
    {
        return await _db.WorkItems
            .Where(w => w.WorkflowInstanceId == workflowInstanceId)
            .OrderBy(w => w.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkItem>> GetByInstanceIdForSuperAdminAsync(
        Guid workflowInstanceId, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters: SuperAdmin history joins work-item comments across tenants.
        return await _db.WorkItems
            
            .Where(w => w.WorkflowInstanceId == workflowInstanceId)
            .OrderBy(w => w.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkItem>> GetOverdueForReminderAsync(
        DateTime asOfUtc, int take, CancellationToken cancellationToken = default)
    {
        return await _db.WorkItems
            .Where(w => w.DueAt != null
                     && w.DueAt < asOfUtc
                     && (w.Status == WorkItemStatus.Pending || w.Status == WorkItemStatus.Claimed))
            .OrderBy(w => w.DueAt)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountOpenClaimedByUserAsync(
        Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
        => _db.WorkItems.CountAsync(
            w => w.OrganizationId == organizationId
                 && w.ClaimedByUserId == userId
                 && w.Status == WorkItemStatus.Claimed,
            cancellationToken);

    public async Task<DateTime?> GetLastClaimedAtByUserAsync(
        Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
    {
        return await _db.WorkItems
            .Where(w => w.OrganizationId == organizationId
                     && w.ClaimedByUserId == userId
                     && w.ClaimedAt != null)
            .OrderByDescending(w => w.ClaimedAt)
            .Select(w => w.ClaimedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(Guid GroupId, string GroupName, int PendingCount, int OverdueCount, int ClaimedCount)>> GetWorkloadByGroupAsync(
        Guid organizationId, DateTime asOfUtc, CancellationToken cancellationToken = default)
    {
        var rows = await (
            from w in _db.WorkItems
            join g in _db.AssignmentGroups on w.AssignmentGroupId equals g.Id
            where w.OrganizationId == organizationId
                  && (w.Status == WorkItemStatus.Pending || w.Status == WorkItemStatus.Claimed)
            group w by new { g.Id, g.Name } into grp
            select new
            {
                GroupId = grp.Key.Id,
                GroupName = grp.Key.Name,
                PendingCount = grp.Count(x => x.Status == WorkItemStatus.Pending),
                OverdueCount = grp.Count(x => x.DueAt != null && x.DueAt < asOfUtc),
                ClaimedCount = grp.Count(x => x.Status == WorkItemStatus.Claimed),
            }).ToListAsync(cancellationToken);

        return rows
            .Select(r => (r.GroupId, r.GroupName, r.PendingCount, r.OverdueCount, r.ClaimedCount))
            .ToList();
    }

    public async Task<(int Open, int Overdue, int CompletedToday)> GetWorkloadTotalsAsync(
        Guid organizationId, DateTime asOfUtc, DateTime todayStartUtc, CancellationToken cancellationToken = default)
    {
        var open = await _db.WorkItems.CountAsync(
            w => w.OrganizationId == organizationId
                 && (w.Status == WorkItemStatus.Pending || w.Status == WorkItemStatus.Claimed),
            cancellationToken);

        var overdue = await _db.WorkItems.CountAsync(
            w => w.OrganizationId == organizationId
                 && (w.Status == WorkItemStatus.Pending || w.Status == WorkItemStatus.Claimed)
                 && w.DueAt != null
                 && w.DueAt < asOfUtc,
            cancellationToken);

        var completedToday = await _db.WorkItems.CountAsync(
            w => w.OrganizationId == organizationId
                 && w.Status == WorkItemStatus.Completed
                 && w.CompletedAt != null
                 && w.CompletedAt >= todayStartUtc,
            cancellationToken);

        return (open, overdue, completedToday);
    }

    public async Task AddAsync(WorkItem workItem, CancellationToken cancellationToken = default)
    {
        await _db.WorkItems.AddAsync(workItem, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);

    public async Task<bool> TryClaimAsync(
        Guid workItemId,
        Guid organizationId,
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var item = await _db.WorkItems
            .FirstOrDefaultAsync(w => w.Id == workItemId && w.OrganizationId == organizationId, cancellationToken);

        if (item is null || item.Status != WorkItemStatus.Pending)
            return false;

        try
        {
            item.Claim(userId, now);
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }
}
