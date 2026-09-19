namespace Workflow.Infrastructure.Services;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Results;
using Workflow.Application.Constants;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

internal sealed partial class WorkflowRuntimeEngine
{
    private async Task<Result> QueueIntegrationActivityAsync(WorkflowInstance instance, ActivityDefinition activity,
        WorkflowExecutionToken? token, string kind, DateTime now, CancellationToken ct)
    {
        if (_integrations is null) return Result.Failure(new Error("Workflow.Integration.Unavailable", "Integration runtime is unavailable."));
        var execution = await CreateActivityInstanceAsync(instance, activity, now, ct);
        execution.AttachToken(token?.Id);
        var variables = BuildInputVariables(await _variableRepo.GetByInstanceIdAsync(instance.Id, ct));
        var result = await _integrations.QueueAsync(instance.Id, execution.Id, kind, activity.ConfigurationJson ?? "{}",
            variables, $"{instance.Id}:{execution.Id}", ct);
        if (result.IsFailure) { execution.Fail(result.Error.Message, now); return result; }
        await _events.AppendAsync(instance.OrganizationId, instance.Id, WorkflowEventType.ActivityStarted, now,
            activity.NodeKey, payloadJson: JsonSerializer.Serialize(new { operation = kind, execution.Id }), cancellationToken: ct);
        instance.AdvanceTo(activity.NodeKey, now);
        return Result.Success();
    }

    public async Task<Result> CompleteExternalActivityAsync(Guid activityInstanceId, IReadOnlyDictionary<string, object?> outputs,
        string outcome, string? error, DateTime now, CancellationToken cancellationToken = default)
    {
        var ai = await _activityRepo.GetByIdAsync(activityInstanceId, cancellationToken);
        if (ai is null) return Result.Failure(new Error("Workflow.Activity.NotFound", "Activity not found."));
        if (ai.Status == ActivityInstanceStatus.Completed) return Result.Success();
        var instance = await _instanceRepo.GetByIdAsync(ai.WorkflowInstanceId, cancellationToken);
        if (instance is null || instance.Status != WorkflowInstanceStatus.Running)
            return Result.Failure(WorkflowErrors.Instance.NotRunning);
        if (ai.Status != ActivityInstanceStatus.Active) return Result.Failure(new Error("Workflow.Activity.NotActive", "Activity is no longer active."));
        var version = await LoadPinnedVersionAsync(instance.PinnedWorkflowVersionId, cancellationToken);
        var activity = version?.Activities.FirstOrDefault(a => a.NodeKey == ai.ActivityNodeKey);
        if (version is null || activity is null) return Result.Failure(WorkflowErrors.Version.NotFound);
        foreach (var (key, value) in outputs)
            await UpsertVariableAsync(instance, key, JsonSerializer.Serialize(value), VariableDataType.Json, now, cancellationToken);
        foreach (var key in new[] { "outcome", "OutcomeKey", "LastActionTaken" })
            await UpsertVariableAsync(instance, key, JsonSerializer.Serialize(outcome), VariableDataType.String, now, cancellationToken);
        var outgoing = version.Transitions.Where(t => t.FromActivityDefinitionId == activity.Id).ToList();
        // An error needs an explicitly conditioned route; an ordinary success arrow
        // must never silently swallow a failed integration or event timeout.
        var eligible = error is null ? outgoing : outgoing.Where(t => !t.IsDefault && !string.IsNullOrWhiteSpace(t.ConditionExpression)).ToList();
        var selectedKey = _transitionEvaluator.Evaluate(eligible, await _variableRepo.GetByInstanceIdAsync(instance.Id, cancellationToken));
        var transition = eligible.FirstOrDefault(t => t.TransitionKey == selectedKey);
        var next = transition is null ? null : version.Activities.FirstOrDefault(a => a.Id == transition.ToActivityDefinitionId);
        if (next is null)
        {
            var message = error ?? "No outgoing transition matches the activity outcome.";
            ai.Fail(message, now); instance.AdvanceTo(ai.ActivityNodeKey, now); instance.Fail(message, now);
            await _events.AppendAsync(instance.OrganizationId, instance.Id, WorkflowEventType.ActivityFailed, now,
                ai.ActivityNodeKey, payloadJson: JsonSerializer.Serialize(new { error = message }), cancellationToken: cancellationToken);
            await _incidentService.OpenAsync(instance.OrganizationId, instance.Id, WorkflowIncidentType.ServiceTaskFailed,
                WorkflowIncidentSeverity.High, message, now, ai.Id, ai.ActivityNodeKey, "Workflow.Integration.Failed", message, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            await ProjectRequestAsync(instance, await _bindingRepo.GetByIdAsync(instance.WorkflowBindingId, cancellationToken), now, cancellationToken);
            return Result.Success(); // failure was durably applied; do not replay delivery
        }
        ai.Complete(now);
        await _events.AppendAsync(instance.OrganizationId, instance.Id, WorkflowEventType.ActivityCompleted, now, ai.ActivityNodeKey,
            payloadJson: JsonSerializer.Serialize(new { outcome }), cancellationToken: cancellationToken);
        await RecordTransitionTakenAsync(instance, ai.ActivityNodeKey, next.NodeKey, transition!, now, cancellationToken);
        var token = ai.ExecutionTokenId is Guid tokenId ? await _tokenRepo.GetByIdAsync(tokenId, cancellationToken) : null;
        instance.AdvanceTo(next.NodeKey, now);
        var result = await AdvanceFromNodeAsync(instance, version, next.NodeKey, now, cancellationToken, token);
        if (result.IsFailure) instance.Fail(result.Error.Message, now);
        await _db.SaveChangesAsync(cancellationToken);
        await ProjectRequestAsync(instance, await _bindingRepo.GetByIdAsync(instance.WorkflowBindingId, cancellationToken), now, cancellationToken);
        // Completion has been committed even if a later node failed. Replaying the
        // external delivery would not repair that downstream failure.
        return Result.Success();
    }
}
