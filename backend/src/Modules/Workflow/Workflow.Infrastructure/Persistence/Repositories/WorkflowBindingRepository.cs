namespace Workflow.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

internal sealed class WorkflowBindingRepository : IWorkflowBindingRepository
{
    private readonly WorkflowDbContext _db;

    public WorkflowBindingRepository(WorkflowDbContext db) => _db = db;

    public Task<WorkflowBinding?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.WorkflowBindings.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public Task<bool> BindingExistsAsync(
        Guid definitionId, Guid organizationId, string moduleKey, string entityType, string triggerEvent,
        CancellationToken cancellationToken = default)
        => _db.WorkflowBindings.AnyAsync(b =>
            b.WorkflowDefinitionId == definitionId &&
            b.OrganizationId == organizationId &&
            b.ModuleKey == moduleKey &&
            b.EntityType == entityType &&
            b.TriggerEvent == triggerEvent, cancellationToken);

    public async Task<(IReadOnlyList<WorkflowBinding> Items, int TotalCount)> GetPagedByDefinitionAsync(
        Guid definitionId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _db.WorkflowBindings.Where(b => b.WorkflowDefinitionId == definitionId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(b => b.OrganizationId).ThenBy(b => b.ModuleKey)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<(IReadOnlyList<WorkflowBinding> Items, int TotalCount)> GetPagedByOrganizationAsync(
        Guid organizationId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _db.WorkflowBindings.Where(b => b.OrganizationId == organizationId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(b => b.ModuleKey)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<WorkflowBinding?> GetByIdForSuperAdminAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters: SuperAdmin reads a single binding across all tenants.
        return _db.WorkflowBindings
            
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
    }

    public async Task<(IReadOnlyList<WorkflowBinding> Items, int TotalCount)> GetPagedAllAsync(
        int pageNumber, int pageSize,
        Guid? definitionId, Guid? organizationId,
        CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters: SuperAdmin cross-tenant read — lists all bindings for the catalog page.
        var query = _db.WorkflowBindings.AsQueryable();

        if (definitionId.HasValue)
            query = query.Where(b => b.WorkflowDefinitionId == definitionId.Value);

        if (organizationId.HasValue)
            query = query.Where(b => b.OrganizationId == organizationId.Value);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(b => b.OrganizationId).ThenBy(b => b.ModuleKey)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task AddAsync(WorkflowBinding binding, CancellationToken cancellationToken = default)
        => await _db.WorkflowBindings.AddAsync(binding, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}
