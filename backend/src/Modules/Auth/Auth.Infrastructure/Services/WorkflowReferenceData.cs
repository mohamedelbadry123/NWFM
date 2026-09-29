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
            "departments" => db.Departments.Where(x => x.IsActive).Select(x => new WorkflowLookupItem(x.Id, x.Code, x.NameEn, x.NameAr, null)),
            "field-activity-types" => db.FieldActivityTypes.Where(x => x.IsActive && db.Departments.Any(d => d.Code == x.DepartmentCode && d.IsActive))
                .Select(x => new WorkflowLookupItem(x.Id, x.Code, x.NameEn, x.NameAr, x.DepartmentCode)),
            _ => throw new ArgumentException("Unknown workflow lookup.", nameof(kind))
        };
        // Positional record members cannot be used in SQL predicates after projection.
        var rows = await query.ToListAsync(ct);
        return rows.Where(x => string.IsNullOrWhiteSpace(parentCode) || x.ParentCode == parentCode).OrderBy(x => x.Code).ToList();
    }

    public Task<bool> IsValidFieldActivityAsync(string departmentCode, string fieldActivityCode, CancellationToken ct) =>
        db.Departments.AnyAsync(d => d.Code == departmentCode && d.IsActive && db.FieldActivityTypes.Any(f => f.DepartmentCode == d.Code && f.Code == fieldActivityCode && f.IsActive), ct);
}
