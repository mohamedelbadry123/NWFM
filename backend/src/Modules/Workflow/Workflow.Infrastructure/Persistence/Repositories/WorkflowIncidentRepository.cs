namespace Workflow.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

internal sealed class WorkflowIncidentRepository : IWorkflowIncidentRepository
{
    private readonly WorkflowDbContext _db;

    public WorkflowIncidentRepository(WorkflowDbContext db) => _db = db;

    public Task<WorkflowIncident?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.WorkflowIncidents.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task<IReadOnlyList<WorkflowIncident>> GetOpenByInstanceIdAsync(
        Guid workflowInstanceId, CancellationToken cancellationToken = default)
    {
        return await _db.WorkflowIncidents
            .Where(i => i.WorkflowInstanceId == workflowInstanceId
                     && (i.Status == WorkflowIncidentStatus.Open || i.Status == WorkflowIncidentStatus.InProgress))
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowIncident>> GetByInstanceIdAsync(
        Guid workflowInstanceId, CancellationToken cancellationToken = default)
    {
        return await _db.WorkflowIncidents
            .Where(i => i.WorkflowInstanceId == workflowInstanceId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowIncident>> GetByStatusAsync(
        Guid organizationId, WorkflowIncidentStatus status, CancellationToken cancellationToken = default)
    {
        return await _db.WorkflowIncidents
            .Where(i => i.OrganizationId == organizationId && i.Status == status)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<WorkflowIncident> Items, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        Guid? organizationId = null,
        WorkflowIncidentStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.WorkflowIncidents.AsQueryable();

        if (organizationId.HasValue && organizationId.Value != Guid.Empty)
            query = query.Where(i => i.OrganizationId == organizationId.Value);

        if (status.HasValue)
            query = query.Where(i => i.Status == status.Value);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task AddAsync(WorkflowIncident incident, CancellationToken cancellationToken = default)
    {
        await _db.WorkflowIncidents.AddAsync(incident, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}
