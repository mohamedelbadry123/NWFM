using Microsoft.EntityFrameworkCore;
using Workflow.Application.Abstractions;
using Workflow.Infrastructure.Persistence;

namespace Workflow.Infrastructure.Services;

internal sealed class WorkflowDraftLock(WorkflowDbContext db) : IWorkflowDraftLock
{
    public Task<T> RunAsync<T>(Guid versionId, Func<Task<T>> action, CancellationToken ct) =>
        WorkflowExecutionLock.RunAsync(db, "version:" + versionId, async () =>
        {
            var cached = db.WorkflowVersions.Local.FirstOrDefault(v => v.Id == versionId);
            if (cached is not null && db.Entry(cached).State == EntityState.Unchanged) await db.Entry(cached).ReloadAsync(ct);
            return await action();
        }, ct);
}
