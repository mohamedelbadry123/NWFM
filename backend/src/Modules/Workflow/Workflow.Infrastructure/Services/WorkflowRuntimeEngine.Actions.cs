namespace Workflow.Infrastructure.Services;

using System.Text.Json;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Results;
using Workflow.Application.Helpers;
using Workflow.Application.Integrations;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

internal sealed partial class WorkflowRuntimeEngine
{
    private async Task<Result> ExecuteHooksAsync(WorkflowInstance instance, ActivityDefinition definition, ActivityInstance execution,
        ActionExecutionTrigger trigger, string? outcome, DateTime now, CancellationToken ct)
    {
        foreach (var hook in definition.Actions.Where(a => a.IsActive && a.ExecutionTrigger == trigger
            && (trigger != ActionExecutionTrigger.OnOutcome || string.Equals(a.OutcomeKey, outcome, StringComparison.OrdinalIgnoreCase))).OrderBy(a => a.Sequence).ThenBy(a => a.Id))
        {
            var variables = await _variableRepo.GetByInstanceIdAsync(instance.Id, ct);
            if (!string.IsNullOrWhiteSpace(hook.ConditionExpression) && !WorkflowCondition.Evaluate(hook.ConditionExpression,
                variables.ToDictionary(v => v.VariableName, v => v.ValueJson, StringComparer.OrdinalIgnoreCase))) continue;
            var provider = _actionRegistry.Resolve(hook.ActionKey);
            if (provider is null || hook.ActionKey == "http.request") return Result.Failure(new Error("Workflow.Hook.Unavailable", "Select an installed task action; use a separate Service Task for HTTP calls."));
            WorkflowActionExecutionResult result;
            var input = BuildInputVariables(variables);
            try
            {
                if (!string.IsNullOrWhiteSpace(hook.InputMappingJson)) input = IntegrationValueMapper.Map(JsonSerializer.Serialize(new { variables = input }), WorkflowTaskForm.Mappings(hook.InputMappingJson));
                var context = new WorkflowActionExecutionContext(instance.OrganizationId, instance.Id, hook.ActionKey, input,
                    $"hook:{execution.Id}:{hook.Id}", definition.ConfigurationJson, execution.Id);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(hook.TimeoutSeconds > 0 ? Math.Min(hook.TimeoutSeconds, 30) : 30));
                var attempts = hook.FailurePolicy == ActionFailurePolicy.Retry ? Math.Clamp(hook.RetryCount + 1, 1, 4) : 1;
                result = WorkflowActionExecutionResult.Failed("Workflow.Hook.Failed", "Task action failed.");
                for (var attempt = 0; attempt < attempts; attempt++)
                {
                    result = await provider.ExecuteAsync(context, timeout.Token);
                    if (result.Success || !result.IsRetryable) break;
                    if (attempt + 1 < attempts) await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(hook.RetryDelaySeconds, 0, 5)), timeout.Token);
                }
                if (result.Success)
                {
                    var outputs = string.IsNullOrWhiteSpace(hook.OutputMappingJson) ? result.OutputVariables
                        : IntegrationValueMapper.Map(JsonSerializer.Serialize(new { output = result.OutputVariables }), WorkflowTaskForm.Mappings(hook.OutputMappingJson));
                    foreach (var (key, value) in outputs) await UpsertVariableAsync(instance, key, JsonSerializer.Serialize(value), VariableDataType.Json, now, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { result = WorkflowActionExecutionResult.Failed("Workflow.Hook.Failed", "Task action failed or timed out. Check its configuration."); }
            await _events.AppendAsync(instance.OrganizationId, instance.Id, result.Success ? WorkflowEventType.ServiceTaskExecuted : WorkflowEventType.ActivityFailed,
                now, definition.NodeKey, payloadJson: JsonSerializer.Serialize(new { actionKey = hook.ActionKey, trigger = trigger.ToString(), success = result.Success }), cancellationToken: ct);
            if (result.Success) continue;
            if (hook.FailurePolicy is ActionFailurePolicy.CreateIncident or ActionFailurePolicy.FailActivity or ActionFailurePolicy.FailWorkflow or ActionFailurePolicy.Retry)
                await _incidentService.OpenAsync(instance.OrganizationId, instance.Id, WorkflowIncidentType.ServiceTaskFailed,
                    WorkflowIncidentSeverity.High, "Task action failed", now, execution.Id, definition.NodeKey, result.ErrorCode, result.ErrorMessage, ct);
            if (hook.FailurePolicy is ActionFailurePolicy.Continue or ActionFailurePolicy.CreateIncident) continue;
            execution.Fail(result.ErrorMessage ?? "Task action failed", now); instance.AdvanceTo(definition.NodeKey, now); instance.Fail(result.ErrorMessage ?? "Task action failed", now);
            if (_activityEvents is not null) await _activityEvents.QueueAsync(instance, definition, execution, "OnFailure", "failure", ct);
            if (trigger != ActionExecutionTrigger.OnFailure) await ExecuteHooksAsync(instance, definition, execution, ActionExecutionTrigger.OnFailure, outcome, now, ct);
            return Result.Failure(new Error("Workflow.Hook.Failed", result.ErrorMessage ?? "Task action failed"));
        }
        return Result.Success();
    }
}

internal sealed class WorkflowVariableActionProvider : IWorkflowActionProvider
{
    public IReadOnlyList<WorkflowActionDescriptor> GetDescriptors() => [new("workflow.setVariables", "Map workflow variables", "ربط متغيرات سير العمل", "Workflow")];
    public Task<WorkflowActionExecutionResult> ExecuteAsync(WorkflowActionExecutionContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(WorkflowActionExecutionResult.Succeeded(context.InputVariables));
}
