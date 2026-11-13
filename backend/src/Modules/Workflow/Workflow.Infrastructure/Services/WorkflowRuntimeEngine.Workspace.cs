using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Results;
using Workflow.Application.Integrations;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

namespace Workflow.Infrastructure.Services;

internal sealed partial class WorkflowRuntimeEngine
{
    private async Task<Result> CompleteTaskTypeAsync(WorkflowInstance instance, ActivityDefinition activity, ActivityInstance execution, DateTime now, CancellationToken ct)
    {
        if (_activityEvents is not null)
        {
            var ready = await _activityEvents.QueueAsync(instance, activity, execution, "OnComplete", "child-completed", ct);
            if (ready.IsFailure) return Result.Failure(ready.Error);
            if (!ready.Value) { execution.SetPhase("WaitingForTaskTypeEvents"); await _db.SaveChangesAsync(ct); return Result.Success(); }
        }
        var outputs = new Dictionary<string, object?>();
        var version = await LoadPinnedVersionAsync(instance.PinnedWorkflowVersionId, ct);
        if (Workflow.Application.Routing.RoutingDefinition.Enabled(version?.WorkspaceJson))
        {
            var child = await _db.WorkflowInstances.Where(c => c.ParentActivityInstanceId == execution.Id && c.Status == WorkflowInstanceStatus.Completed).OrderByDescending(c => c.StartedAt).FirstOrDefaultAsync(ct);
            if (child is not null)
            {
                var childValues = new Dictionary<string, object?>(BuildInputVariables(await _variableRepo.GetByInstanceIdAsync(child.Id, ct)));
                var childScope = await _db.RoutingScopes.FindAsync([child.Id], ct);
                if (childScope is not null) foreach (var pair in RoutingValues(childScope)) childValues[pair.Key] = pair.Value;
                outputs = IntegrationValueMapper.Map(JsonSerializer.Serialize(childValues), ParseCallActivityConfig(activity.ConfigurationJson).OutputMappings);
            }
        }
        return await CompleteExternalActivityAsync(execution.Id, outputs, "success", null, now, ct);
    }

    public async Task ResumeActivityEventsAsync(Guid activityId, CancellationToken ct)
    {
        var execution = await _db.ActivityInstances.FindAsync([activityId], ct);
        if (execution is null || execution.Status != ActivityInstanceStatus.Active || !await WorkflowTreeGuard.CanRunAsync(_db, execution.WorkflowInstanceId, ct)) return;
        var instance = await _instanceRepo.GetByIdAsync(execution.WorkflowInstanceId, ct);
        if (instance is null) return;
        var version = await LoadPinnedVersionAsync(instance.PinnedWorkflowVersionId, ct);
        var activity = version?.Activities.FirstOrDefault(a => a.NodeKey == execution.ActivityNodeKey);
        if (version is null || activity is null) return;
        if (await _db.MappingInvocations.AnyAsync(i => i.OriginActivityId == execution.Id && i.ExecutionKind == "RequiredEvent" && !i.Applied, ct)) return;
        if (await _db.IntegrationJobs.AnyAsync(j => j.ActivityInstanceId == execution.Id && j.ExecutionKind != "Callable" && j.IsActivityEvent && j.Required && j.Status != "Completed", ct)) return;
        var now = DateTime.UtcNow;
        if (execution.Phase == "WaitingForTaskTypeEvents")
            await CompleteTaskTypeAsync(instance, activity, execution, now, ct);
        else if (execution.Phase == "WaitingForOutcomeEvents")
        {
            var item = await _db.WorkItems.FirstOrDefaultAsync(w => w.ActivityInstanceId == execution.Id && w.Status == WorkItemStatus.Completed, ct);
            if (item is not null) await AdvanceAsync(instance.Id, item.Id, now, ct);
        }
        else if (execution.Phase == "WaitingForEnterEvents")
        {
            Result result;
            if (activity.ActivityType == ActivityType.MainActivity)
                result = await ExecuteCallActivityAsync(instance, version, activity, now, ct,
                    execution.ExecutionTokenId is Guid token ? await _tokenRepo.GetByIdAsync(token, ct) : null, execution);
            else
            {
                result = await ExecuteHooksAsync(instance, activity, execution, ActionExecutionTrigger.OnEnter, null, now, ct);
                if (result.IsSuccess) result = await CreateApprovalTaskAsync(instance, activity, execution, now, ct);
            }
            if (result.IsFailure)
            {
                execution.Fail(result.Error.Message, now); instance.Fail(result.Error.Message, now);
                if (_activityEvents is not null) await _activityEvents.QueueAsync(instance, activity, execution, "OnFailure", "failure", ct);
            }
        }
        await _db.SaveChangesAsync(ct);
    }
}
