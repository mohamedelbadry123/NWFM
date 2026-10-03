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
            // Activity types belong to no department, so a department passed as the parent narrows nothing.
            "field-activity-types" => db.FieldActivityTypes.Where(x => x.IsActive)
                .Select(x => new WorkflowLookupItem(x.Id, x.Code, x.NameEn, x.NameAr, null)),
            _ => throw new ArgumentException("Unknown workflow lookup.", nameof(kind))
        };
        var rows = await query.ToListAsync(ct);
        return rows.OrderBy(x => x.Code).ToList();
    }

    public Task<bool> IsValidFieldActivityAsync(string departmentCode, string fieldActivityCode, CancellationToken ct) =>
        db.Departments.AnyAsync(d => d.Code == departmentCode && d.IsActive && db.FieldActivityTypes.Any(f => f.Code == fieldActivityCode && f.IsActive), ct);
}
