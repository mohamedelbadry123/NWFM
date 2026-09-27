using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Results;
using Workflow.Domain.Enums;

namespace Workflow.Infrastructure.Services;

internal sealed partial class WorkflowRuntimeEngine
{
    public async Task ResumeActivityEventsAsync(Guid activityId, CancellationToken ct)
    {
        var execution = await _db.ActivityInstances.FindAsync([activityId], ct);
        if (execution is null || execution.Status != ActivityInstanceStatus.Active || !await WorkflowTreeGuard.CanRunAsync(_db, execution.WorkflowInstanceId, ct)) return;
        var instance = await _instanceRepo.GetByIdAsync(execution.WorkflowInstanceId, ct);
        if (instance is null) return;
        var version = await LoadPinnedVersionAsync(instance.PinnedWorkflowVersionId, ct);
        var activity = version?.Activities.FirstOrDefault(a => a.NodeKey == execution.ActivityNodeKey);
        if (version is null || activity is null) return;
        if (await _db.IntegrationJobs.AnyAsync(j => j.ActivityInstanceId == execution.Id && j.IsActivityEvent && j.Required && j.Status != "Completed", ct)) return;
        var now = DateTime.UtcNow;
        if (execution.Phase == "WaitingForOutcomeEvents")
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
