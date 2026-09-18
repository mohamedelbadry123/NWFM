namespace Workflow.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

internal sealed class SlaPolicyRepository : ISlaPolicyRepository
{
    private readonly WorkflowDbContext _db;

    public SlaPolicyRepository(WorkflowDbContext db) => _db = db;

    public Task<SlaPolicy?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.SlaPolicies.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<SlaPolicy?> GetByCodeAsync(
        string policyCode, Guid? organizationId, CancellationToken cancellationToken = default)
        => _db.SlaPolicies.FirstOrDefaultAsync(
            p => p.PolicyCode == policyCode && p.OrganizationId == organizationId,
            cancellationToken);

    public async Task<(IReadOnlyList<SlaPolicy> Items, int TotalCount)> GetPagedAsync(
        int pageNumber, int pageSize, string? searchTerm = null, CancellationToken cancellationToken = default)
    {
        var query = _db.SlaPolicies.AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim();
            query = query.Where(p =>
                p.PolicyCode.Contains(term) || p.Name.Contains(term) ||
                (p.NameAr != null && p.NameAr.Contains(term)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(p => p.PolicyCode)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task AddAsync(SlaPolicy policy, CancellationToken cancellationToken = default)
    {
        await _db.SlaPolicies.AddAsync(policy, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}
