namespace Workflow.Application.Helpers;

using System.Globalization;
using System.Text.Json;
using NWFM.Shared.Integration.Workflow;
using Workflow.Application.DTOs;
using Workflow.Application.Models;
using Workflow.Domain.Enums;
using Workflow.Application.Integrations;

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
            foreach (var action in activity.Actions)
            {
                if (type is not (ActivityType.UserTask or ActivityType.MainActivity)) Error("ACTION_TRIGGER_UNSUPPORTED", "Task action hooks are supported on human activities. Use activity events for integrations.");
                if (action.ActionKey == "http.request" || string.IsNullOrWhiteSpace(action.ActionKey) || registry is not null && registry.Resolve(action.ActionKey) is null)
                    Error("TASK_ACTION_UNAVAILABLE", "Select an installed task action. HTTP calls belong in a separate Service Task.");
                if (!Enum.TryParse<ActionExecutionTrigger>(action.ExecutionTriggerName, out _)) Error("ACTION_TRIGGER_INVALID", "Select a valid task action trigger.");
                if (!Enum.TryParse<ActionFailurePolicy>(action.FailurePolicyName, out _)) Error("ACTION_POLICY_INVALID", "Select a valid task action failure policy.");
                if (action.RetryCount is < 0 or > 3 || action.RetryDelaySeconds is < 0 or > 5 || action.TimeoutSeconds is < 0 or > 30)
                    Error("ACTION_LIMITS_INVALID", "Task hooks allow up to three retries, a five-second delay and a 30-second total timeout. Use an integration activity for long-running work.");
                if (!string.IsNullOrWhiteSpace(action.ConditionExpression) && !WorkflowCondition.IsValid(action.ConditionExpression)) Error("ACTION_CONDITION_INVALID", "The task action condition is invalid.");
                try { WorkflowTaskForm.Mappings(action.InputMappingJson); WorkflowTaskForm.Mappings(action.OutputMappingJson); }
                catch (JsonException) { Error("ACTION_MAPPING_INVALID", "Action mappings must be JSON objects of destination keys and source paths."); }
            }

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
                    case ActivityType.UserTask:
                    case ActivityType.MainActivity:
                        if (root.TryGetProperty("nodePriority", out var priority) && priority.ValueKind == JsonValueKind.Number && (!priority.TryGetDecimal(out var priorityValue) || priorityValue != 0))
                            Error("TASK_PRIORITY_UNSUPPORTED", "Task queue priority is not supported. Set it to zero; routing priority is configured on transitions.");
                        foreach (var flag in new[] { "requiresClaim", "allowSelfClaim" })
                            if (root.TryGetProperty(flag, out var claimFlag) && claimFlag.ValueKind == JsonValueKind.False)
                                Error("TASK_CLAIM_UNSUPPORTED", "Tasks use group-member claiming. Disabling claiming or self-claim is not supported.");
                        if (root.TryGetProperty("formSchemaJson", out var schema) && schema.ValueKind != JsonValueKind.Null && schema.ToString().Length > 0)
                            Error("TASK_SCHEMA_UNSUPPORTED", "Use the form field editor. Arbitrary JSON Schema forms are not supported.");
                        if (activity.Outcomes.Any(o => o.RequiresAttachment))
                            Error("TASK_ATTACHMENT_UNSUPPORTED", "Required attachments need an upload provider, which is not installed. Remove this requirement before publishing.");
                        foreach (var rule in activity.AssignmentRules)
                        {
                            if (!string.Equals(rule.AssigneeTypeName, "AssignmentGroup", StringComparison.OrdinalIgnoreCase)) Error("TASK_ASSIGNEE_UNSUPPORTED", "Assign a workflow group; individual, role and department assignment rules are not supported.");
                            if (!string.IsNullOrWhiteSpace(rule.Expression) && !WorkflowCondition.IsValid(rule.Expression)) Error("ASSIGNMENT_CONDITION_INVALID", "Assignment conditions must use the supported comparison language.");
                        }
                        try
                        {
                            var formError = WorkflowTaskForm.ValidateConfiguration(WorkflowTaskForm.Parse(root.GetRawText()));
                            if (formError is not null) Error("TASK_FORM_INVALID", formError);
                        }
                        catch (JsonException) { Error("TASK_FORM_INVALID", "Task form fields and mappings contain invalid values."); }
                        break;
                    case ActivityType.ServiceTask when activity.ActionKey == "http.request":
                        foreach (var issue in IntegrationConfigurationRules.Validate("Http", root.GetRawText())) Error("HTTP_CONFIG_INVALID", issue);
                        break;
                    case ActivityType.WaitEvent:
                        var eventKey = root.TryGetProperty("eventKey", out _) ? Text("eventKey") : Text("signalKey");
                        if (string.IsNullOrWhiteSpace(eventKey))
                            Error("EVENT_KEY_REQUIRED", "Wait for Event requires a non-empty event key.");
                        if (Guid.TryParse(Text("connectionId"), out var connectionId) && connectionId != Guid.Empty)
                        {
                            foreach (var issue in IntegrationConfigurationRules.Validate("Webhook", root.GetRawText())) Error("EVENT_CONFIG_INVALID", issue);
                        }
                        else if (root.TryGetProperty("connectionId", out _) || !string.IsNullOrWhiteSpace(Text("correlationVariable")))
                            Error("EVENT_CONNECTION_REQUIRED", "Select a webhook connection to match events by a correlation variable.");
                        else warnings.Add(new("EVENT_INGRESS_REQUIRED", "This legacy event wait accepts internal triggers only. Select a webhook connection for external callbacks.", activity.NodeKey));
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
                        if (root.TryGetProperty("versionId", out var childVersion) && childVersion.ValueKind != JsonValueKind.Null
                            && (childVersion.ValueKind != JsonValueKind.String || !Guid.TryParse(childVersion.GetString(), out _))) Error("CALL_VERSION_INVALID", "A fixed child version must be a published version ID.");
                        foreach (var key in new[] { "inputMappings", "outputMappings" })
                            if (root.TryGetProperty(key, out var mapping) && (mapping.ValueKind != JsonValueKind.Object || mapping.EnumerateObject().Any(p => p.Value.ValueKind != JsonValueKind.String)))
                                Error("CALL_MAPPING_INVALID", "Child mappings require an object of variable names and source paths.");
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
                        if (!Enum.TryParse<NotificationFailurePolicy>(policy, true, out var parsedPolicy) || !Enum.IsDefined(parsedPolicy))
                            Error("NOTIFICATION_POLICY_INVALID", "Select a valid notification failure policy.");
                        foreach (var issue in IntegrationConfigurationRules.Validate("Email", root.GetRawText())) Error("NOTIFICATION_CONFIG_INVALID", issue);
                        break;
                }
            }
        }
    }
}
