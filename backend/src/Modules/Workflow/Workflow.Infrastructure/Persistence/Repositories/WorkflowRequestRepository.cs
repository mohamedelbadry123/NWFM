namespace Workflow.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

internal sealed class WorkflowRequestRepository : IWorkflowRequestRepository
{
    private readonly WorkflowDbContext _db;

    public WorkflowRequestRepository(WorkflowDbContext db) => _db = db;

    public Task<WorkflowRequest?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken cancellationToken = default)
        => _db.WorkflowRequests.FirstOrDefaultAsync(
            r => r.Id == id && r.OrganizationId == organizationId, cancellationToken);

    public Task<WorkflowRequest?> GetByInstanceIdAsync(
        Guid workflowInstanceId, Guid organizationId, CancellationToken cancellationToken = default)
        => _db.WorkflowRequests.FirstOrDefaultAsync(
            r => r.WorkflowInstanceId == workflowInstanceId && r.OrganizationId == organizationId,
            cancellationToken);

    public async Task<IReadOnlyList<WorkflowRequest>> GetByInstanceIdsAsync(
        Guid organizationId, IReadOnlyCollection<Guid> instanceIds, CancellationToken cancellationToken = default)
    {
        if (instanceIds.Count == 0)
            return [];

        return await _db.WorkflowRequests
            .Where(r => r.OrganizationId == organizationId && instanceIds.Contains(r.WorkflowInstanceId))
            .ToListAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<WorkflowRequest> Items, int TotalCount)> GetPagedAsync(
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
        CancellationToken cancellationToken = default)
    {
        var query = _db.WorkflowRequests.Where(r => r.OrganizationId == organizationId);

        if (status is not null)
            query = query.Where(r => r.Status == status);

        if (originalGroupId is not null)
            query = query.Where(r => r.OriginalAssignedGroupId == originalGroupId);

        if (fromUtc is not null)
            query = query.Where(r => r.RequestDate >= fromUtc);

        if (toUtc is not null)
            query = query.Where(r => r.RequestDate <= toUtc);

        if (!string.IsNullOrWhiteSpace(service))
        {
            var svc = service.Trim().ToLower();
            query = query.Where(r =>
                r.ServiceKey.ToLower().Contains(svc)
                || r.ServiceNameEn.ToLower().Contains(svc)
                || (r.ServiceNameAr != null && r.ServiceNameAr.ToLower().Contains(svc)));
        }

        if (!string.IsNullOrWhiteSpace(currentStep))
        {
            var step = currentStep.Trim().ToLower();
            query = query.Where(r =>
                (r.CurrentActivityNameEn != null && r.CurrentActivityNameEn.ToLower().Contains(step))
                || (r.CurrentActivityNameAr != null && r.CurrentActivityNameAr.ToLower().Contains(step)));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(r =>
                r.RequestNumber.ToLower().Contains(term)
                || r.ServiceNameEn.ToLower().Contains(term)
                || (r.ServiceNameAr != null && r.ServiceNameAr.ToLower().Contains(term))
                || r.BusinessEntityId.ToLower().Contains(term)
                || (r.CurrentActivityNameEn != null && r.CurrentActivityNameEn.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(slaStatus))
        {
            var warningUntil = asOfUtc.AddHours(1);
            query = slaStatus.Trim().ToLowerInvariant() switch
            {
                "breached" => query.Where(r =>
                    r.Status == WorkflowInstanceStatus.Running
                    && r.CurrentTaskDueAtUtc != null
                    && r.CurrentTaskDueAtUtc < asOfUtc),
                "warning" => query.Where(r =>
                    r.Status == WorkflowInstanceStatus.Running
                    && r.CurrentTaskDueAtUtc != null
                    && r.CurrentTaskDueAtUtc >= asOfUtc
                    && r.CurrentTaskDueAtUtc <= warningUntil),
                "ok" => query.Where(r =>
                    r.Status != WorkflowInstanceStatus.Running
                    || r.CurrentTaskDueAtUtc == null
                    || r.CurrentTaskDueAtUtc > warningUntil),
                _ => query
            };
        }

        var total = await query.CountAsync(cancellationToken);
        var ordered = string.Equals(sortBy, "remainingSla", StringComparison.OrdinalIgnoreCase)
            ? query.OrderBy(r => r.CurrentTaskDueAtUtc ?? DateTime.MaxValue).ThenByDescending(r => r.RequestDate)
            : query.OrderByDescending(r => r.RequestDate);
        var items = await ordered
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public Task<int> CountByOrgAndYearAsync(Guid organizationId, int year, CancellationToken cancellationToken = default)
    {
        var start = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddYears(1);
        return _db.WorkflowRequests.CountAsync(
            r => r.OrganizationId == organizationId && r.RequestDate >= start && r.RequestDate < end,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetInstanceIdsMissingProjectionAsync(
        Guid organizationId, int take, CancellationToken cancellationToken = default)
    {
        return await _db.WorkflowInstances
            .Where(i => i.OrganizationId == organizationId)
            .Where(i => !_db.WorkflowRequests.Any(r => r.WorkflowInstanceId == i.Id))
            .OrderBy(i => i.StartedAt)
            .Select(i => i.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<(int Total, int InProgress, int Completed, int Breached)> GetKpisAsync(
        Guid organizationId, DateTime asOfUtc, CancellationToken cancellationToken = default)
    {
        var total = await _db.WorkflowRequests.CountAsync(r => r.OrganizationId == organizationId, cancellationToken);
        var inProgress = await _db.WorkflowRequests.CountAsync(
            r => r.OrganizationId == organizationId && r.Status == WorkflowInstanceStatus.Running,
            cancellationToken);
        var completed = await _db.WorkflowRequests.CountAsync(
            r => r.OrganizationId == organizationId && r.Status == WorkflowInstanceStatus.Completed,
            cancellationToken);
        var breached = await _db.WorkflowRequests.CountAsync(
            r => r.OrganizationId == organizationId
                 && r.Status == WorkflowInstanceStatus.Running
                 && r.CurrentTaskDueAtUtc != null
                 && r.CurrentTaskDueAtUtc < asOfUtc,
            cancellationToken);
        return (total, inProgress, completed, breached);
    }

    public async Task AddAsync(WorkflowRequest request, CancellationToken cancellationToken = default)
        => await _db.WorkflowRequests.AddAsync(request, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}
