namespace Workflow.Infrastructure.Services;

using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Domain.Entities;
using Workflow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

internal sealed class SerializedWorkflowRuntimeEngine(WorkflowRuntimeEngine engine, WorkflowDbContext db) : IWorkflowRuntimeEngine
{
    public Task<Result<WorkflowInstance>> StartAsync(Guid organizationId, Guid workflowBindingId, string businessEntityId, string idempotencyKey,
        DateTime now, string? correlationId = null, Guid? startedByUserId = null, Guid? parentInstanceId = null, string? parentActivityNodeKey = null,
        Guid? pinnedWorkflowVersionId = null, CancellationToken cancellationToken = default)
        => WorkflowExecutionLock.RunAsync(db, $"start:{organizationId}:{idempotencyKey}", () => engine.StartAsync(organizationId, workflowBindingId, businessEntityId,
            idempotencyKey, now, correlationId, startedByUserId, parentInstanceId, parentActivityNodeKey, pinnedWorkflowVersionId, cancellationToken), cancellationToken);
    public Task<Result> AdvanceAsync(Guid workflowInstanceId, Guid completedWorkItemId, DateTime now, CancellationToken cancellationToken = default)
        => WorkflowExecutionLock.RunAsync(db, "instance:" + workflowInstanceId, async () =>
        {
            // Command handlers may already have loaded this instance before taking the lock.
            foreach (var entry in db.ChangeTracker.Entries<WorkflowInstance>().Where(e => e.Entity.Id == workflowInstanceId && e.State == EntityState.Unchanged))
                await entry.ReloadAsync(cancellationToken);
            return await engine.AdvanceAsync(workflowInstanceId, completedWorkItemId, now, cancellationToken);
        }, cancellationToken);
    public async Task<Result> ResumeFromTimerAsync(Guid timerId, DateTime now, CancellationToken cancellationToken = default)
    {
        var timer = await db.WorkflowTimers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == timerId, cancellationToken);
        return await WorkflowExecutionLock.RunAsync(db, "instance:" + timer?.WorkflowInstanceId,
            () => engine.ResumeFromTimerAsync(timerId, now, cancellationToken), cancellationToken);
    }
    public Task<Result> ResumeFromExternalSignalAsync(Guid workflowInstanceId, string? signalKey, DateTime now, CancellationToken cancellationToken = default)
        => WorkflowExecutionLock.RunAsync(db, "instance:" + workflowInstanceId, () => engine.ResumeFromExternalSignalAsync(workflowInstanceId, signalKey, now, cancellationToken), cancellationToken);
    public Task<Result> ResumeFromCallActivityAsync(Guid parentInstanceId, string callActivityNodeKey, DateTime now, CancellationToken cancellationToken = default)
        => WorkflowExecutionLock.RunAsync(db, "instance:" + parentInstanceId, () => engine.ResumeFromCallActivityAsync(parentInstanceId, callActivityNodeKey, now, cancellationToken), cancellationToken);
    public async Task<Result> CompleteExternalActivityAsync(Guid activityInstanceId, IReadOnlyDictionary<string, object?> outputs, string outcome,
        string? error, DateTime now, CancellationToken cancellationToken = default)
    {
        var activity = await db.ActivityInstances.AsNoTracking().FirstOrDefaultAsync(a => a.Id == activityInstanceId, cancellationToken);
        return await WorkflowExecutionLock.RunAsync(db, "instance:" + activity?.WorkflowInstanceId,
            () => engine.CompleteExternalActivityAsync(activityInstanceId, outputs, outcome, error, now, cancellationToken), cancellationToken);
    }
}
