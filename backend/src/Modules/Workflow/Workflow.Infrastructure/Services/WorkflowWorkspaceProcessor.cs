using Microsoft.EntityFrameworkCore;
using Workflow.Application.Abstractions;
using Workflow.Domain.Enums;
using Workflow.Infrastructure.Persistence;

namespace Workflow.Infrastructure.Services;

internal sealed class WorkflowWorkspaceProcessor(WorkflowDbContext db, WorkflowRuntimeEngine runtime, WorkflowActivityEvents events)
{
    public async Task ProcessAsync(CancellationToken ct)
    {
        var ids = await db.ActivityInstances.AsNoTracking().Where(a => a.Status == ActivityInstanceStatus.Active
            && ((a.Phase == "WaitingForEnterEvents" || a.Phase == "WaitingForOutcomeEvents")
                && !db.IntegrationJobs.Any(j => j.ActivityInstanceId == a.Id && j.IsActivityEvent && j.Required && j.Status != "Completed")
                || a.DueAt <= DateTime.UtcNow && a.SlaBreachedAt == null || a.NextSlaAlertAt <= DateTime.UtcNow)
            && db.WorkflowInstances.Any(i => i.Id == a.WorkflowInstanceId && i.Status == WorkflowInstanceStatus.Running))
            .OrderBy(a => a.StartedAt).Take(100).Select(a => new { a.Id, a.WorkflowInstanceId }).ToListAsync(ct);
        foreach (var row in ids)
            await WorkflowExecutionLock.RunAsync(db, "instance:" + row.WorkflowInstanceId, async () =>
            {
                if (!await WorkflowTreeGuard.CanRunAsync(db, row.WorkflowInstanceId, ct)) return false;
                var execution = await db.ActivityInstances.FindAsync([row.Id], ct);
                if (execution is null || execution.Status != ActivityInstanceStatus.Active) return false;
                var instance = await db.WorkflowInstances.FindAsync([execution.WorkflowInstanceId], ct);
                var definition = await db.ActivityDefinitions.FirstOrDefaultAsync(a => a.WorkflowVersionId == instance!.PinnedWorkflowVersionId && a.NodeKey == execution.ActivityNodeKey, ct);
                if (instance is null || definition is null) return false;
                var snapshot = Workflow.Application.Workspace.WorkspaceDesign.Configuration(definition.ConfigurationJson)["publishedSla"];
                if (snapshot is not null)
                {
                    var sla = System.Text.Json.JsonSerializer.Deserialize<Workflow.Application.Workspace.PublishedSla>(snapshot.ToJsonString(), Workflow.Application.Integrations.IntegrationJson.Options)!;
                    var schedule = Workflow.Application.Workspace.PublishedSlaClock.Alerts(sla, execution.StartedAt);
                    while (execution.SlaAlertCount < schedule.Length && schedule[execution.SlaAlertCount].At <= DateTime.UtcNow)
                    {
                        var alert = schedule[execution.SlaAlertCount];
                        var result = await events.QueueAsync(instance, definition, execution, alert.Trigger, alert.Key, ct);
                        if (result.IsFailure) break;
                        if (alert.Trigger == "OnSlaBreach") execution.MarkSlaBreached(DateTime.UtcNow);
                        db.WorkflowEvents.Add(Workflow.Domain.Entities.WorkflowEvent.Append(instance.OrganizationId, instance.Id,
                            alert.Trigger == "OnSlaBreach" ? WorkflowEventType.SlaBreached : WorkflowEventType.ReminderSent,
                            DateTime.UtcNow, execution.ActivityNodeKey, payloadJson: System.Text.Json.JsonSerializer.Serialize(new { execution.Id, alert.Key, alert.At })));
                        execution.AdvanceSlaAlert(schedule.ElementAtOrDefault(execution.SlaAlertCount + 1)?.At);
                    }
                }
                else if (execution.DueAt <= DateTime.UtcNow && execution.SlaBreachedAt is null)
                {
                    var queued = await events.QueueAsync(instance, definition, execution, "OnSlaBreach", "sla", ct);
                    if (queued.IsSuccess)
                    {
                        execution.MarkSlaBreached(DateTime.UtcNow);
                        db.WorkflowEvents.Add(Workflow.Domain.Entities.WorkflowEvent.Append(instance.OrganizationId, instance.Id,
                            WorkflowEventType.SlaBreached, DateTime.UtcNow, execution.ActivityNodeKey,
                            payloadJson: System.Text.Json.JsonSerializer.Serialize(new { execution.Id, execution.DueAt })));
                    }
                }
                await runtime.ResumeActivityEventsAsync(execution.Id, ct);
                await db.SaveChangesAsync(ct);
                return true;
            }, ct);

        // Failure notifications remain deliverable when the activity has stopped the workflow.
        // The operation key and root lock make this recovery scan safe across worker restarts.
        var failed = await db.ActivityInstances.AsNoTracking().Where(a => a.Status == ActivityInstanceStatus.Failed
            && !db.IntegrationJobs.Any(j => j.ActivityInstanceId == a.Id && j.IsActivityEvent && j.EventTrigger == "OnFailure")
            && db.WorkflowInstances.Any(i => i.Id == a.WorkflowInstanceId && (i.Status == WorkflowInstanceStatus.Running || i.Status == WorkflowInstanceStatus.Failed)
                    && db.ActivityDefinitions.Any(d => d.WorkflowVersionId == i.PinnedWorkflowVersionId
                    && d.ConfigurationJson != null && d.ConfigurationJson.Contains("OnFailure"))))
            .OrderBy(a => a.StartedAt).Take(100).Select(a => new { a.Id, a.WorkflowInstanceId }).ToListAsync(ct);
        foreach (var row in failed)
            await WorkflowExecutionLock.RunAsync(db, "instance:" + row.WorkflowInstanceId, async () =>
            {
                if (!await WorkflowTreeGuard.CanRunAsync(db, row.WorkflowInstanceId, ct, allowCompleted: true)) return false;
                var execution = await db.ActivityInstances.FindAsync([row.Id], ct);
                var instance = await db.WorkflowInstances.FindAsync([row.WorkflowInstanceId], ct);
                if (execution is null || instance is null || execution.Status != ActivityInstanceStatus.Failed) return false;
                var definition = await db.ActivityDefinitions.FirstAsync(a => a.WorkflowVersionId == instance.PinnedWorkflowVersionId && a.NodeKey == execution.ActivityNodeKey, ct);
                await events.QueueAsync(instance, definition, execution, "OnFailure", "failure", ct);
                return true;
            }, ct);
    }
}
