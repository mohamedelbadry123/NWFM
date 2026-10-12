using Workflow.Application.DTOs;
using Workflow.Application.Helpers;
using Workflow.Application.Workspace;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

namespace Workflow.Infrastructure.Services;

internal sealed partial class WorkflowWorkspacePublisher
{
    private static void ValidateFormRouteTermination(WorkflowVersion version, List<WorkflowValidationIssueDto> errors)
    {
        var edges = new List<(string From, string To)>();
        var states = new List<(string Key, string Activity, string Name)>();
        string Node(string key) => "node:" + key;
        string Form(string node, string step) => "form:" + node + ":" + step;
        foreach (var activity in version.Activities)
        {
            BusinessActivityConfiguration config;
            try { config = Workflow.Application.Integrations.IntegrationJson.Read<BusinessActivityConfiguration>(activity.ConfigurationJson); }
            catch (System.Text.Json.JsonException) { continue; }
            if (activity.ActivityType is ActivityType.MainActivity or ActivityType.UserTask && config.FormSteps.Count is > 0 and <= 100)
            {
                edges.Add((Node(activity.NodeKey), Form(activity.NodeKey, config.FormSteps[0].Id)));
                foreach (var step in config.FormSteps)
                {
                    var key = Form(activity.NodeKey, step.Id); states.Add((key, activity.NodeKey, step.Name));
                    foreach (var destination in step.Rules.Take(100).Select(r => r.Destination).Append(step.Otherwise))
                    {
                        try
                        {
                            var route = WorkflowFormRouting.ResolveDestination(config.FormSteps, step, destination, version, activity.NodeKey);
                            if (route.FormStepId is not null) edges.Add((key, Form(activity.NodeKey, route.FormStepId)));
                            else if (route.NodeKey is not null) edges.Add((key, Node(route.NodeKey)));
                            else foreach (var transition in version.Transitions.Where(t => t.FromActivityDefinitionId == activity.Id))
                                if (version.Activities.FirstOrDefault(a => a.Id == transition.ToActivityDefinitionId) is { } target) edges.Add((key, Node(target.NodeKey)));
                        }
                        catch (FormatException) { /* Invalid destinations have their own diagnostics. */ }
                    }
                }
            }
            else foreach (var transition in version.Transitions.Where(t => t.FromActivityDefinitionId == activity.Id))
                if (version.Activities.FirstOrDefault(a => a.Id == transition.ToActivityDefinitionId) is { } target) edges.Add((Node(activity.NodeKey), Node(target.NodeKey)));
        }
        var reachesEnd = version.Activities.Where(a => a.ActivityType == ActivityType.End).Select(a => Node(a.NodeKey)).ToHashSet();
        bool changed;
        do { changed = false; foreach (var edge in edges) if (reachesEnd.Contains(edge.To)) changed |= reachesEnd.Add(edge.From); } while (changed);
        foreach (var state in states.Where(s => !reachesEnd.Contains(s.Key)))
            errors.Add(new("ROUTING_NO_END", $"{state.Name}: Configure a potential path to workflow End.", state.Activity));
    }

    private async Task ValidateFormStepsAsync(WorkflowVersion version, ActivityDefinition activity, BusinessActivityConfiguration config,
        Guid taskTypeId, List<WorkflowValidationIssueDto> errors, CancellationToken ct)
    {
        void Error(string code, string message) => errors.Add(new(code, message, activity.NodeKey));
        if (config.FormStepsReviewRequired) Error("ACTIVITY_FORM_REVIEW", "Review the configured forms after changing Task Type, then confirm in the Form tab.");
        if (config.FormSteps.Count > 100) { Error("ROUTING_LIMIT", "Configure at most 100 forms per activity."); return; }
        var memberships = await taskTypes!.FormIdsAsync(taskTypeId, ct);
        if (config.FormSteps.Any(s => string.IsNullOrWhiteSpace(s.Id)) || config.FormSteps.Select(s => s.Id).Distinct().Count() != config.FormSteps.Count)
            Error("ROUTING_FORM_ID", "Each configured form needs a unique step identifier.");
        foreach (var step in config.FormSteps)
        {
            var label = string.IsNullOrWhiteSpace(step.Name) ? step.Id : step.Name;
            if (step.ReviewRequired) Error("ROUTING_FORM_REVIEW", $"{label}: Review and confirm its rules after changing the published form version.");
            if (!memberships.Contains(step.FormId)) { Error("ACTIVITY_FORM_MEMBERSHIP", $"{label}: The form no longer belongs to this Task Type."); continue; }
            if (forms is null || step.VersionNo <= 0 || await forms.FindPublishedAsync(step.FormId, ct) is not { AcceptsSubmissions: true }
                || await forms.GetVersionSchemaAsync(step.FormId, step.VersionNo, ct) is null)
            { Error("ACTIVITY_FORM_UNAVAILABLE", $"{label}: Select an available published form version."); continue; }
            if (step.Rules.Count > 100) { Error("ROUTING_LIMIT", $"{label}: Configure at most 100 rules per form."); continue; }
            if (step.Rules.Any(r => string.IsNullOrWhiteSpace(r.Id)) || step.Rules.Select(r => r.Id).Distinct().Count() != step.Rules.Count)
                Error("ROUTING_RULE_ID", $"{label}: Each rule needs a unique identifier.");
            var fields = await forms.GetFieldsAsync(step.FormId, step.VersionNo, ct);
            foreach (var rule in step.Rules)
            {
                foreach (var message in WorkflowFormRouting.Validate(rule.When, fields))
                    Error("ROUTING_CONDITION", $"{label}, rule '{rule.Name}' ({rule.Id}): {message}");
                ValidateDestination(rule.Destination, $"rule '{rule.Name}' ({rule.Id})");
            }
            ValidateDestination(step.Otherwise, "Otherwise");
            void ValidateDestination(FormRouteDestination destination, string description)
            {
                try
                {
                    var route = WorkflowFormRouting.ResolveDestination(config.FormSteps, step, destination, version, activity.NodeKey);
                    if (route == (null, null) && !version.Transitions.Any(t => t.FromActivityDefinitionId == activity.Id))
                        Error("ROUTING_DESTINATION", $"{label}, {description}: Connect the final form's forward route or select a destination.");
                }
                catch (FormatException e) { Error("ROUTING_DESTINATION", $"{label}, {description}: {e.Message}"); }
            }
        }
        // Each form needs at least one potential exit, possibly through other forms.
        foreach (var origin in config.FormSteps)
        {
            var seen = new HashSet<string>();
            bool CanExit(ActivityFormStep current)
            {
                if (!seen.Add(current.Id)) return false;
                foreach (var destination in current.Rules.Select(r => r.Destination).Append(current.Otherwise))
                {
                    try
                    {
                        var route = WorkflowFormRouting.ResolveDestination(config.FormSteps, current, destination, version, activity.NodeKey);
                        if (route.FormStepId is null) return true;
                        var next = config.FormSteps.First(s => s.Id == route.FormStepId);
                        if (CanExit(next)) return true;
                    }
                    catch (FormatException) { /* Destination diagnostics are already attached to the rule. */ }
                }
                return false;
            }
            if (!CanExit(origin)) Error("ROUTING_NO_EXIT", $"{origin.Name}: Configure a path that can leave the activity.");
        }
    }
}
