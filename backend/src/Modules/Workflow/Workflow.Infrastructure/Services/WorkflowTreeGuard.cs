using Microsoft.EntityFrameworkCore;
using Workflow.Domain.Enums;
using Workflow.Infrastructure.Persistence;

namespace Workflow.Infrastructure.Services;

internal static class WorkflowTreeGuard
{
    public static async Task<bool> CanRunAsync(WorkflowDbContext db, Guid instanceId, CancellationToken ct, bool allowCompleted = false)
    {
        for (var depth = 0; depth < 17; depth++)
        {
            var instance = await db.WorkflowInstances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == instanceId, ct);
            // New instances may not yet have been flushed by the enclosing transaction.
            instance ??= db.WorkflowInstances.Local.FirstOrDefault(i => i.Id == instanceId);
            if (instance is null || (instance.Status != WorkflowInstanceStatus.Running && !(allowCompleted && instance.Status is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Failed))) return false;
            if (instance.ParentInstanceId is not Guid parent) return true;
            instanceId = parent;
        }
        return false;
    }

    public static async Task<Guid> RootAsync(WorkflowDbContext db, Guid id, CancellationToken ct)
    {
        var seen = new HashSet<Guid>();
        while (seen.Add(id))
        {
            var parent = await db.WorkflowInstances.AsNoTracking().Where(i => i.Id == id).Select(i => i.ParentInstanceId).FirstOrDefaultAsync(ct);
            if (parent is null) return id;
            id = parent.Value;
        }
        throw new InvalidOperationException("Cyclic workflow instance hierarchy.");
    }
}
