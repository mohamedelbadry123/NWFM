using System.Text.Json;
using Workflow.Application.DTOs;
using Workflow.Application.Integrations;
using Workflow.Application.Workspace;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

namespace Workflow.Infrastructure.Services;

internal sealed partial class WorkflowWorkspacePublisher
{
    internal static void ValidateHierarchy(WorkflowVersion version, WorkflowWorkspaceDefinition settings, List<WorkflowValidationIssueDto> errors)
    {
        if (settings.Kind == "Child" && (settings.ParentDefinitionId is null || string.IsNullOrWhiteSpace(settings.ParentNodeKey)))
            errors.Add(new("CHILD_OWNER", "Create the child workflow inside its main workflow."));
        foreach (var node in version.Activities)
        {
            void Error(string code, string message) => errors.Add(new(code, message, node.NodeKey));
            if (node.ActivityType is not (ActivityType.Start or ActivityType.End or ActivityType.MainActivity or ActivityType.UserTask or ActivityType.ServiceTask or ActivityType.NotificationTask or ActivityType.WaitEvent)
                && !(settings.DesignerVersion == 4 && (Workflow.Application.Routing.RoutingGraphRules.IsGate(node.ActivityType.ToString()) || node.ActivityType is ActivityType.Timer or ActivityType.ScriptTask)))
                Error("HIERARCHY_ELEMENT", "Use Task Types, Steps, and Events between Start and End.");
            if (settings.Kind == "Main" && node.ActivityType == ActivityType.UserTask)
                Error("MAIN_STEP", "Steps belong inside a Task Type's child workflow.");
            if (settings.Kind == "Child" && node.ActivityType is ActivityType.MainActivity or ActivityType.CallActivity)
                Error("CHILD_TASK_TYPE", "Task Types belong in the main workflow.");
            if (node.ActivityType is ActivityType.ServiceTask or ActivityType.NotificationTask)
            {
                try
                {
                    var delivery = WorkspaceDesign.Configuration(node.ConfigurationJson);
                    var mode = delivery["executionMode"]?.GetValue<string>();
                    if (mode is not (null or "Sync" or "Async")) Error("EVENT_MODE", "Select Sync or Async event execution.");
                    if (mode == "Sync" && delivery["required"]?.GetValue<bool>() == false)
                        Error("EVENT_SYNC_REQUIRED", "Sync events must succeed before advancing.");
                    var binding = WorkspaceDesign.Binding(node.ConfigurationJson);
                    if (binding?.Trigger is "OnApprove" or "OnReject" or "OnComment"
                        && version.Activities.Any(a => a.NodeKey == binding.SourceNodeKey && a.ActivityType == ActivityType.MainActivity))
                        Error("TASK_TYPE_TRIGGER", "Task Types advance when their child completes. Select a lifecycle trigger such as completion.");
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException) { Error("EVENT_CONFIG", "Event settings are invalid."); }
            }
            if (node.ActivityType is not (ActivityType.MainActivity or ActivityType.UserTask)) continue;
            try
            {
                var config = IntegrationJson.Read<Workflow.Application.Workspace.BusinessActivityConfiguration>(node.ConfigurationJson);
                if (node.ActivityType == ActivityType.MainActivity && (config.FormSteps.Count > 0 || config.FormDefinitionId is not null))
                    Error("TASK_TYPE_FORM", "Attach forms to the child workflow's steps.");
                if (node.ActivityType == ActivityType.UserTask)
                {
                    if (config.TaskTypeId != settings.TaskTypeId)
                        Error("STEP_TASK_TYPE", "A step must use its child workflow's Task Type.");
                    if (config.FormSteps.Count != 1 || config.FormDefinitionId is not null)
                        Error("STEP_SINGLE_FORM", "Link exactly one published form to each step.");
                }
            }
            catch (JsonException) { Error("ACTIVITY_CONFIG", "Activity settings are invalid."); }
        }
    }
}
