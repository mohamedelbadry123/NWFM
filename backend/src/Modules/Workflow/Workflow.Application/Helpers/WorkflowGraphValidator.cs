namespace Workflow.Application.Helpers;

using Workflow.Application.DTOs;
using Workflow.Application.Models;
using Workflow.Domain.Enums;

/// <summary>
/// Shared graph rules for Validate and Publish so the designer badge and Publish agree.
/// </summary>
public static class WorkflowGraphValidator
{
    public static void Validate(
        WorkflowXmlDocument doc,
        List<WorkflowValidationIssueDto> errors,
        List<WorkflowValidationIssueDto> warnings)
    {
        var nodeKeys = new HashSet<string>(doc.Activities.Select(a => a.NodeKey));

        var startNodes = doc.Activities.Where(a =>
            string.Equals(a.ActivityTypeName, ActivityType.Start.ToString(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        var endNodes = doc.Activities.Where(a =>
            string.Equals(a.ActivityTypeName, ActivityType.End.ToString(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (startNodes.Count == 0)
            errors.Add(new WorkflowValidationIssueDto("NO_START_NODE", "Workflow must have exactly one Start node."));
        else if (startNodes.Count > 1)
            errors.Add(new WorkflowValidationIssueDto("MULTIPLE_START_NODES", "Workflow must have exactly one Start node."));

        if (endNodes.Count == 0)
            errors.Add(new WorkflowValidationIssueDto("NO_END_NODE", "Workflow must have at least one End node."));

        foreach (var a in doc.Activities)
        {
            if (string.IsNullOrWhiteSpace(a.NodeKey))
                errors.Add(new WorkflowValidationIssueDto("EMPTY_NODE_KEY", "All activities must have a non-empty NodeKey."));
        }

        var duplicateKeys = doc.Activities
            .GroupBy(a => a.NodeKey)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
        foreach (var key in duplicateKeys)
            errors.Add(new WorkflowValidationIssueDto("DUPLICATE_NODE_KEY", $"Duplicate NodeKey: {key}.", key));

        foreach (var t in doc.Transitions)
        {
            if (!nodeKeys.Contains(t.FromNodeKey))
                errors.Add(new WorkflowValidationIssueDto("INVALID_TRANSITION_FROM",
                    $"Transition '{t.Key}' references unknown FromNodeKey '{t.FromNodeKey}'."));
            if (!nodeKeys.Contains(t.ToNodeKey))
                errors.Add(new WorkflowValidationIssueDto("INVALID_TRANSITION_TO",
                    $"Transition '{t.Key}' references unknown ToNodeKey '{t.ToNodeKey}'."));
        }

        var outgoing = doc.Transitions.GroupBy(t => t.FromNodeKey).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var a in doc.Activities)
        {
            var typeName = a.ActivityTypeName;
            var isEnd = string.Equals(typeName, ActivityType.End.ToString(), StringComparison.OrdinalIgnoreCase);
            var isStart = string.Equals(typeName, ActivityType.Start.ToString(), StringComparison.OrdinalIgnoreCase);

            if (!isEnd && !outgoing.ContainsKey(a.NodeKey))
                warnings.Add(new WorkflowValidationIssueDto("NO_OUTGOING_TRANSITION",
                    $"Node '{a.NodeKey}' has no outgoing transitions.", a.NodeKey));

            if (!isStart)
            {
                var hasIncoming = doc.Transitions.Any(t => t.ToNodeKey == a.NodeKey);
                if (!hasIncoming)
                    warnings.Add(new WorkflowValidationIssueDto("NO_INCOMING_TRANSITION",
                        $"Node '{a.NodeKey}' has no incoming transitions.", a.NodeKey));
            }
        }

        var userTasks = doc.Activities.Where(a =>
            string.Equals(a.ActivityTypeName, ActivityType.UserTask.ToString(), StringComparison.OrdinalIgnoreCase));
        foreach (var ut in userTasks)
        {
            var hasGroup = ut.AssignmentRules.Any(r =>
                !string.IsNullOrWhiteSpace(r.ReferenceId) || !string.IsNullOrWhiteSpace(r.AssignmentKey));
            if (!hasGroup && string.IsNullOrWhiteSpace(ut.AssignmentGroupId) && string.IsNullOrWhiteSpace(ut.AssignmentKey))
            {
                errors.Add(new WorkflowValidationIssueDto("USER_TASK_NO_ASSIGNMENT",
                    $"UserTask '{ut.NodeKey}' must assign an organization group.", ut.NodeKey));
            }
        }

        ValidateGateways(doc, errors, warnings, ActivityType.ExclusiveGateway);
        ValidateGateways(doc, errors, warnings, ActivityType.InclusiveGateway);

        var callActivities = doc.Activities.Where(a =>
            string.Equals(a.ActivityTypeName, ActivityType.CallActivity.ToString(), StringComparison.OrdinalIgnoreCase));
        foreach (var call in callActivities)
        {
            if (string.IsNullOrWhiteSpace(call.ConfigurationJson)
                || !call.ConfigurationJson.Contains("definitionKey", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new WorkflowValidationIssueDto("CALL_ACTIVITY_CONFIG",
                    $"CallActivity '{call.NodeKey}' must include ConfigurationJson with definitionKey.", call.NodeKey));
            }
        }

        foreach (var a in doc.Activities)
        {
            if (!Enum.TryParse<ActivityType>(a.ActivityTypeName, true, out _))
            {
                errors.Add(new WorkflowValidationIssueDto("UNKNOWN_ACTIVITY_TYPE",
                    $"Unknown activity type '{a.ActivityTypeName}' on node '{a.NodeKey}'.", a.NodeKey));
            }
        }
    }

    private static void ValidateGateways(
        WorkflowXmlDocument doc,
        List<WorkflowValidationIssueDto> errors,
        List<WorkflowValidationIssueDto> warnings,
        ActivityType type)
    {
        var typeName = type.ToString();
        var gateways = doc.Activities.Where(a =>
            string.Equals(a.ActivityTypeName, typeName, StringComparison.OrdinalIgnoreCase));

        foreach (var gw in gateways)
        {
            var gwOutgoing = doc.Transitions.Where(t => t.FromNodeKey == gw.NodeKey).ToList();
            if (gwOutgoing.Count < 2)
            {
                var issue = new WorkflowValidationIssueDto(
                    $"{typeName.ToUpperInvariant()}_OUTGOING",
                    $"{typeName} '{gw.NodeKey}' should have at least two outgoing transitions.",
                    gw.NodeKey);
                if (type == ActivityType.InclusiveGateway)
                    errors.Add(issue);
                else
                    warnings.Add(issue);
            }

            var defaults = gwOutgoing.Where(t => t.IsDefault).ToList();
            if (gwOutgoing.Count >= 2 && defaults.Count != 1)
            {
                var issue = new WorkflowValidationIssueDto(
                    $"{typeName.ToUpperInvariant()}_DEFAULT",
                    $"{typeName} '{gw.NodeKey}' should mark exactly one outgoing arrow as default (fallback).",
                    gw.NodeKey);
                if (type == ActivityType.InclusiveGateway)
                    errors.Add(issue);
                else
                    warnings.Add(issue);
            }
        }
    }
}
