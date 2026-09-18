namespace Workflow.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

internal sealed class WorkflowDefinitionRepository : IWorkflowDefinitionRepository
{
    private readonly WorkflowDbContext _db;

    public WorkflowDefinitionRepository(WorkflowDbContext db) => _db = db;

    public Task<WorkflowDefinition?> GetByIdAsync(
        Guid id, CancellationToken cancellationToken = default)
        // IgnoreQueryFilters: SuperAdmin designer reads a definition by id across tenants.
        // OrgAdmin handlers must still verify OrganizationId against the JWT tenant.
        => _db.WorkflowDefinitions
            
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public Task<WorkflowDefinition?> GetByKeyAsync(
        Guid organizationId, string definitionKey, CancellationToken cancellationToken = default)
        => _db.WorkflowDefinitions
            
            .FirstOrDefaultAsync(
                d => d.OrganizationId == organizationId && d.DefinitionKey == definitionKey,
                cancellationToken);

    public Task<bool> KeyExistsAsync(
        Guid organizationId, string definitionKey, CancellationToken cancellationToken = default)
        => _db.WorkflowDefinitions
            
            .AnyAsync(
                d => d.OrganizationId == organizationId && d.DefinitionKey == definitionKey,
                cancellationToken);

    public Task<bool> HasVersionsAsync(
        Guid definitionId, CancellationToken cancellationToken = default)
        => _db.WorkflowVersions
            .AnyAsync(v => v.WorkflowDefinitionId == definitionId, cancellationToken);

    public Task<int> GetVersionCountAsync(
        Guid definitionId, CancellationToken cancellationToken = default)
        => _db.WorkflowVersions
            .CountAsync(v => v.WorkflowDefinitionId == definitionId, cancellationToken);

    public async Task<(IReadOnlyList<WorkflowDefinition> Items, int TotalCount)> GetPagedAsync(
        int pageNumber, int pageSize, string? searchTerm, Guid? organizationId,
        CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters: SuperAdmin catalog. Callers pass OrganizationId for tenant scope.
        var query = _db.WorkflowDefinitions.AsQueryable();

        if (organizationId.HasValue && organizationId.Value != Guid.Empty)
            query = query.Where(d => d.OrganizationId == organizationId.Value);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLower();
            query = query.Where(d =>
                d.Name.ToLower().Contains(term) ||
                d.DefinitionKey.ToLower().Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(d => d.Name)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task AddAsync(WorkflowDefinition definition, CancellationToken cancellationToken = default)
        => await _db.WorkflowDefinitions.AddAsync(definition, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}
