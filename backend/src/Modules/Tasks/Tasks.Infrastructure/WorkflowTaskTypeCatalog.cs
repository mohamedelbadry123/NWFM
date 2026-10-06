using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Integration.Workflow;
using Tasks.Infrastructure.Persistence;

namespace Tasks.Infrastructure;

internal sealed class WorkflowTaskTypeCatalog(TasksDbContext db) : IWorkflowTaskTypeCatalog
{
    public async Task<IReadOnlyList<WorkflowLookupItem>> ListAsync(CancellationToken ct) =>
        await db.TaskTypes.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.NameEn)
            .Select(t => new WorkflowLookupItem(t.Id, t.Code, t.NameEn, t.NameAr, null)).ToListAsync(ct);

    public Task<bool> IsActiveAsync(Guid id, CancellationToken ct) => db.TaskTypes.AnyAsync(t => t.Id == id && t.IsActive, ct);
}
