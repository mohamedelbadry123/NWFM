namespace Workflow.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

internal sealed class WorkflowInstanceRepository : IWorkflowInstanceRepository
{
    private readonly WorkflowDbContext _db;

    public WorkflowInstanceRepository(WorkflowDbContext db) => _db = db;

    public Task<WorkflowInstance?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public Task<WorkflowInstance?> GetByIdForSuperAdminAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters: SuperAdmin cross-tenant read of a single workflow instance.
        return _db.WorkflowInstances
            
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
    }

    public Task<bool> ExistsByIdempotencyKeyAsync(Guid organizationId, string idempotencyKey, CancellationToken cancellationToken = default)
        => _db.WorkflowInstances
            .AnyAsync(i => i.OrganizationId == organizationId && i.IdempotencyKey == idempotencyKey,
                cancellationToken);

    public Task<WorkflowInstance?> GetByIdempotencyKeyAsync(
        Guid organizationId, string idempotencyKey, CancellationToken cancellationToken = default)
        => _db.WorkflowInstances
            .FirstOrDefaultAsync(i => i.OrganizationId == organizationId && i.IdempotencyKey == idempotencyKey,
                cancellationToken);

    public async Task<(IReadOnlyList<WorkflowInstance> Items, int TotalCount)> GetPagedByOrgAsync(
        Guid organizationId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _db.WorkflowInstances.Where(i => i.OrganizationId == organizationId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(i => i.StartedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<(IReadOnlyList<WorkflowInstance> Items, int TotalCount)> GetPagedAllAsync(
        int pageNumber, int pageSize, Guid? organizationId, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters: SuperAdmin sees all instances across all tenants.
        var query = _db.WorkflowInstances.AsQueryable();
        if (organizationId.HasValue)
            query = query.Where(i => i.OrganizationId == organizationId.Value);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(i => i.StartedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<IReadOnlyList<WorkflowInstance>> GetByParentAsync(
        Guid parentInstanceId, CancellationToken cancellationToken = default)
    {
        return await _db.WorkflowInstances
            .Where(i => i.ParentInstanceId == parentInstanceId)
            .OrderBy(i => i.StartedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<WorkflowInstance?> GetByBusinessEntityAsync(
        Guid organizationId, string moduleKey, string entityType, string entityId,
        CancellationToken cancellationToken = default)
    {
        return await (
            from i in _db.WorkflowInstances
            join b in _db.WorkflowBindings on i.WorkflowBindingId equals b.Id
            where i.OrganizationId == organizationId
                && i.BusinessEntityId == entityId
                && b.ModuleKey == moduleKey
                && b.EntityType == entityType
            orderby i.StartedAt descending
            select i
        ).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(WorkflowInstance instance, CancellationToken cancellationToken = default)
    {
        await _db.WorkflowInstances.AddAsync(instance, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
