using Microsoft.EntityFrameworkCore;
using Workflow.Application.DTOs;
using Workflow.Application.Workspace;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

namespace Workflow.Infrastructure.Services;

internal sealed partial class WorkflowWorkspacePublisher
{
    private async Task ValidateSimplifiedAsync(WorkflowVersion version, List<WorkflowValidationIssueDto> errors, CancellationToken ct)
    {
        var organizationId = await db.WorkflowDefinitions.Where(d => d.Id == version.WorkflowDefinitionId).Select(d => d.OrganizationId).SingleAsync(ct);
        var business = version.Activities.Where(a => a.ActivityType is ActivityType.MainActivity or ActivityType.UserTask).ToList();
        bool Reaches(Guid from, Guid to)
        {
            var seen = new HashSet<Guid>(); var pending = new Stack<Guid>(); pending.Push(from);
            while (pending.TryPop(out var current))
            {
                if (!seen.Add(current)) continue;
                foreach (var edge in version.Transitions.Where(t => t.FromActivityDefinitionId == current))
                { if (edge.ToActivityDefinitionId == to) return true; pending.Push(edge.ToActivityDefinitionId); }
            }
            return false;
        }
        foreach (var activity in version.Activities)
        {
            void Error(string code, string message) => errors.Add(new(code, message, activity.NodeKey));
            var config = WorkspaceDesign.Configuration(activity.ConfigurationJson);
            if (business.Contains(activity))
            {
                var department = config["departmentCode"]?.GetValue<string>(); var field = config["fieldActivityCode"]?.GetValue<string>();
                if (!await db.SlaPolicies.AnyAsync(p => p.OrganizationId == organizationId && p.IsActive && p.DepartmentCode == department && p.FieldActivityCode == field
                    && db.BusinessCalendars.Any(c => c.Id == p.BusinessCalendarId && c.IsActive && (c.OrganizationId == null || c.OrganizationId == organizationId)), ct))
                    Error("SLA_MATCH_REQUIRED", "Create an active SLA rule for this Department and Field Activity Type before publishing.");
                if (activity.Outcomes.Count != 2 || !activity.Outcomes.Any(o => o.OutcomeKey == "APPROVE") || !activity.Outcomes.Any(o => o.OutcomeKey == "REJECT" && o.RequiresComment))
                    Error("ACTIONS_REQUIRED", "This workspace requires exactly Accept and Reject; Reject requires a comment.");
                var target = business.FirstOrDefault(a => a.NodeKey == config["rejectTargetNodeKey"]?.GetValue<string>());
                if (target is null || (target.Id == activity.Id ? business.Any(a => a.Id != activity.Id && Reaches(a.Id, activity.Id)) : !Reaches(target.Id, activity.Id) || Reaches(activity.Id, target.Id)))
                    Error("REJECT_PREVIOUS", "Reject must return to an earlier activity. Only the first activity may return to itself.");
                if (version.Transitions.Count(t => t.FromActivityDefinitionId == activity.Id) != 1)
                    Error("ACCEPT_ROUTE", "Connect one forward Accept route from this activity.");
                if (activity.Actions.Count != 0 || config["events"] is System.Text.Json.Nodes.JsonArray { Count: > 0 })
                    Error("LEGACY_ACTIONS", "Adapt legacy automated actions to visible event nodes before publishing.");
            }
            var binding = WorkspaceDesign.Binding(activity.ConfigurationJson);
            if (config.ContainsKey("triggerBinding") && binding is null)
                Error("EVENT_BINDING", "Select a valid source activity and trigger for this event node.");
            if (binding is null) continue;
            if (activity.ActivityType is not (ActivityType.ServiceTask or ActivityType.NotificationTask) || !business.Any(a => a.NodeKey == binding.SourceNodeKey))
                Error("EVENT_SOURCE", "A triggered API, Email or SMS node must reference a business activity in this workflow.");
            if (binding.Trigger is not ("OnEnter" or "OnApprove" or "OnReject" or "OnComment" or "OnComplete" or "OnFailure" or "OnSlaReminder" or "OnSlaBreach"))
                Error("EVENT_TRIGGER", "Select a supported activity trigger.");
            if (version.Transitions.Any(t => t.FromActivityDefinitionId == activity.Id || t.ToActivityDefinitionId == activity.Id))
                Error("EVENT_SEQUENCE", "Activity-triggered event nodes cannot have sequence arrows.");
            if (binding.Trigger is "OnFailure" or "OnSlaReminder" or "OnSlaBreach" && config["required"]?.GetValue<bool>() == true)
                Error("EVENT_BACKGROUND", "Failure and SLA notifications must run in the background.");
        }
    }
}
