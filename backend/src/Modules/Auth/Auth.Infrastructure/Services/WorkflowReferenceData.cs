using Auth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Integration.Workflow;

namespace Auth.Infrastructure.Services;

internal sealed class WorkflowReferenceData(AuthDbContext db) : IWorkflowReferenceData
{
    public async Task<IReadOnlyList<WorkflowLookupItem>> ListAsync(string kind, string? parentCode, CancellationToken ct)
    {
        IQueryable<WorkflowLookupItem> query = kind switch
        {
            "clusters" => db.Clusters.Where(x => x.IsActive).Select(x => new WorkflowLookupItem(x.Id, x.Code, x.NameEn, x.NameAr, null)),
            "regions" => db.Cbus.Where(x => x.IsActive && db.Clusters.Any(c => c.Code == x.ClusterCode && c.IsActive))
                .Select(x => new WorkflowLookupItem(x.Id, x.Code, x.NameEn, x.NameAr, x.ClusterCode)),
            "cities" => db.Branches.Where(x => x.IsActive && db.Cbus.Any(r => r.Code == x.CbuCode && r.IsActive && db.Clusters.Any(c => c.Code == r.ClusterCode && c.IsActive)))
                .Select(x => new WorkflowLookupItem(x.Id, x.Code, x.NameEn, x.NameAr, x.CbuCode)),
            "departments" => db.Departments.Where(x => x.IsActive).Select(x => new WorkflowLookupItem(x.Id, x.Code, x.NameEn, x.NameAr, null)),
            "field-activity-types" => db.FieldActivityTypes.Where(x => x.IsActive && db.Departments.Any(d => d.Code == x.DepartmentCode && d.IsActive))
                .Select(x => new WorkflowLookupItem(x.Id, x.Code, x.NameEn, x.NameAr, x.DepartmentCode)),
            _ => throw new ArgumentException("Unknown workflow lookup.", nameof(kind))
        };
        // Positional record members cannot be used in SQL predicates after projection.
        var rows = await query.ToListAsync(ct);
        return rows.Where(x => string.IsNullOrWhiteSpace(parentCode) || x.ParentCode == parentCode).OrderBy(x => x.Code).ToList();
    }

    public Task<bool> IsValidGeographyAsync(WorkflowGeography g, CancellationToken ct) =>
        db.Clusters.AnyAsync(c => c.Code == g.ClusterCode && c.IsActive && db.Cbus.Any(r => r.Code == g.RegionCode && r.ClusterCode == c.Code && r.IsActive
            && db.Branches.Any(b => b.Code == g.CityCode && b.CbuCode == r.Code && b.IsActive)), ct);

    public Task<bool> IsValidFieldActivityAsync(string departmentCode, string fieldActivityCode, CancellationToken ct) =>
        db.Departments.AnyAsync(d => d.Code == departmentCode && d.IsActive && db.FieldActivityTypes.Any(f => f.DepartmentCode == d.Code && f.Code == fieldActivityCode && f.IsActive), ct);
}
