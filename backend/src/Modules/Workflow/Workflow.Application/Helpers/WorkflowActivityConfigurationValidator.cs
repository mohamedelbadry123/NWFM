namespace Workflow.Application.Helpers;

using System.Globalization;
using System.Text.Json;
using NWFM.Shared.Integration.Workflow;
using Workflow.Application.DTOs;
using Workflow.Application.Models;
using Workflow.Domain.Enums;

/// <summary>Configuration checks shared by Validate and Publish; never execute an integration.</summary>
public static class WorkflowActivityConfigurationValidator
{
    public static void Validate(WorkflowXmlDocument document, List<WorkflowValidationIssueDto> errors,
        List<WorkflowValidationIssueDto> warnings, IWorkflowActionRegistry? registry = null)
    {
        foreach (var activity in document.Activities)
        {
            void Error(string code, string message) => errors.Add(new(code, message, activity.NodeKey));
            if (!Enum.TryParse<ActivityType>(activity.ActivityTypeName, true, out var type)) continue;

            if (type == ActivityType.ServiceTask)
            {
                if (string.IsNullOrWhiteSpace(activity.ActionKey))
                    Error("SERVICE_ACTION_REQUIRED", "Select a registered action for this service task.");
                else if (registry is not null && registry.Resolve(activity.ActionKey) is null)
                    Error("SERVICE_ACTION_UNAVAILABLE", $"Action '{activity.ActionKey}' is not installed on this server.");
            }

            JsonDocument json;
            try
            {
                json = JsonDocument.Parse(string.IsNullOrWhiteSpace(activity.ConfigurationJson) ? "{}" : activity.ConfigurationJson);
            }
            catch (JsonException)
            {
                Error("ACTIVITY_CONFIG_JSON", "Activity configuration must contain valid JSON.");
                continue;
            }
            using (json)
            {
                var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    Error("ACTIVITY_CONFIG_OBJECT", "Activity configuration must be a JSON object.");
                    continue;
                }
                string? Text(string key) => root.TryGetProperty(key, out var value)
                    && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

                switch (type)
                {
                    case ActivityType.WaitEvent:
                        var eventKey = root.TryGetProperty("eventKey", out _) ? Text("eventKey") : Text("signalKey");
                        if (string.IsNullOrWhiteSpace(eventKey))
                            Error("EVENT_KEY_REQUIRED", "Wait for Event requires a non-empty event key.");
                        if (!string.IsNullOrWhiteSpace(Text("correlationVariable")))
                            Error("EVENT_CORRELATION_UNSUPPORTED", "Variable-based event correlation is not implemented yet; do not publish a wait that relies on it.");
                        warnings.Add(new("EVENT_INGRESS_REQUIRED", "Event waits currently require the internal trigger service. External callbacks are not yet available.", activity.NodeKey));
                        break;
                    case ActivityType.Timer:
                        var timerType = Text("timerType") ?? "Duration";
                        if (timerType.Equals("Duration", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!TimeSpan.TryParse(Text("duration"), CultureInfo.InvariantCulture, out var duration) || duration < TimeSpan.Zero)
                                Error("TIMER_DURATION_INVALID", "Enter a non-negative timer duration, for example 00:30:00.");
                        }
                        else if (timerType.Equals("DueDate", StringComparison.OrdinalIgnoreCase))
                        {
                            var dueAt = Text("dueAt");
                            if (string.IsNullOrWhiteSpace(dueAt)
                                || !(dueAt.EndsWith('Z') || System.Text.RegularExpressions.Regex.IsMatch(dueAt, @"[+-]\d{2}:\d{2}$"))
                                || !DateTimeOffset.TryParse(dueAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                                Error("TIMER_DATE_INVALID", "A due date must be a valid date and time with a timezone offset.");
                        }
                        else if (timerType.Equals("ExternalSignal", StringComparison.OrdinalIgnoreCase))
                            Error("TIMER_SIGNAL_UNSUPPORTED", "Use Wait for Event for external signals. External-signal timers have no supported delivery path.");
                        else
                            Error("TIMER_TYPE_INVALID", "Select Duration or DueDate for a timer.");
                        break;
                    case ActivityType.CallActivity:
                        if (string.IsNullOrWhiteSpace(Text("definitionKey")))
                            Error("CALL_ACTIVITY_CONFIG", "Call Activity requires a non-empty child definition key.");
                        if (root.TryGetProperty("waitForCompletion", out var wait) && wait.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                            Error("CALL_ACTIVITY_WAIT_INVALID", "Wait for completion must be true or false.");
                        break;
                    case ActivityType.ScriptTask:
                        var hasAssignments = root.TryGetProperty("setVariables", out var assignments)
                            || root.TryGetProperty("assignments", out assignments);
                        if (!hasAssignments || assignments.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                            Error("VARIABLE_ASSIGNMENTS_INVALID", "Set Variables requires an object of variable assignments or the legacy name/value array.");
                        else if (assignments.ValueKind == JsonValueKind.Array && assignments.EnumerateArray().Any(item =>
                            item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("name", out var name)
                            || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString())
                            || !item.TryGetProperty("value", out _)))
                            Error("VARIABLE_ASSIGNMENTS_INVALID", "Each variable assignment requires a name and value.");
                        break;
                    case ActivityType.NotificationTask:
                        if (string.IsNullOrWhiteSpace(Text("templateKey")))
                            Error("NOTIFICATION_TEMPLATE_REQUIRED", "Select a notification template.");
                        var policy = Text("failurePolicy") ?? Text("notificationFailurePolicy") ?? "Continue";
                        if (policy.Equals("Retry", StringComparison.OrdinalIgnoreCase))
                            Error("NOTIFICATION_RETRY_UNSUPPORTED", "Automatic notification retries are not implemented yet. Select Continue or FailWorkflow.");
                        else if (!Enum.TryParse<NotificationFailurePolicy>(policy, true, out var parsedPolicy) || !Enum.IsDefined(parsedPolicy))
                            Error("NOTIFICATION_POLICY_INVALID", "Select a valid notification failure policy.");
                        break;
                }
            }
        }
    }
}
