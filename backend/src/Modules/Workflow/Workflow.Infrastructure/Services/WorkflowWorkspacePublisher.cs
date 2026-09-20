using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;
using Workflow.Application.Workspace;
using Workflow.Application.Integrations;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;
using Workflow.Infrastructure.Persistence;

namespace Workflow.Infrastructure.Services;

internal sealed class WorkflowWorkspacePublisher(WorkflowDbContext db, IWorkflowReferenceData references,
    IWorkflowVersionRepository versions) : IWorkflowWorkspacePublisher
{
    public async Task<IReadOnlyList<WorkflowValidationIssueDto>> ValidateAsync(WorkflowVersion version, CancellationToken ct)
    {
        var errors = new List<WorkflowValidationIssueDto>();
        if (string.IsNullOrWhiteSpace(version.WorkspaceJson))
        {
            if (version.Activities.Any(a => a.ActivityType == ActivityType.MainActivity))
                errors.Add(new("WORKSPACE_REQUIRED", "Select the workflow kind and geography before publishing main activities."));
            return errors;
        }
        WorkflowWorkspaceDefinition? workspace;
        try { workspace = JsonSerializer.Deserialize<WorkflowWorkspaceDefinition>(version.WorkspaceJson, IntegrationJson.Options); }
        catch (JsonException) { errors.Add(new("WORKSPACE_INVALID", "Workflow settings are invalid.")); return errors; }
        if (workspace is null || workspace.Kind is not ("Main" or "Child"))
        { errors.Add(new("WORKSPACE_KIND", "Select Main or Child workflow.")); return errors; }
        if (workspace.Kind == "Main" && !await references.IsValidGeographyAsync(new(workspace.ClusterCode ?? "", workspace.RegionCode ?? "", workspace.CityCode ?? ""), ct))
            errors.Add(new("WORKSPACE_GEOGRAPHY", "Select an active cluster, region and city in the same hierarchy."));
        if (workspace.Kind == "Child" && new[] { workspace.ClusterCode, workspace.RegionCode, workspace.CityCode }.Any(x => !string.IsNullOrEmpty(x)))
            errors.Add(new("WORKSPACE_INHERITANCE", "Child workflows inherit geography; remove local geography selections."));
        if (workspace.Kind == "Main" && !version.Activities.Any(a => a.ActivityType == ActivityType.MainActivity))
            errors.Add(new("WORKSPACE_MAIN_ACTIVITY", "Add at least one main activity with a child workflow."));

        foreach (var activity in version.Activities)
        {
            BusinessActivityConfiguration config;
            try { config = IntegrationJson.Read<BusinessActivityConfiguration>(activity.ConfigurationJson); }
            catch (JsonException) { errors.Add(new("ACTIVITY_CONFIG", "Activity settings are invalid.", activity.NodeKey)); continue; }
            void Error(string code, string message) => errors.Add(new(code, message, activity.NodeKey));
            if (activity.ActivityType is ActivityType.UserTask or ActivityType.MainActivity)
            {
                if (!await references.IsValidFieldActivityAsync(config.DepartmentCode ?? "", config.FieldActivityCode ?? "", ct))
                    Error("ACTIVITY_FIELD_TYPE", "Select an active department and one of its Field Activity Types.");
                foreach (var rule in activity.AssignmentRules.Where(r => r.IsActive))
                    if (rule.ReferenceId is Guid group && !await db.AssignmentGroups.AnyAsync(g => g.Id == group && g.IsActive, ct))
                        Error("ACTIVITY_GROUP", "Select an active assigned group in this tenant.");
                if (config.SlaDurationHours is null or <= 0 && config.SlaPolicyId is null)
                    Error("ACTIVITY_SLA", "Set a positive SLA duration or choose an SLA policy.");
                if (config.SlaPolicyId is Guid policyId && !await db.SlaPolicies.AnyAsync(p => p.Id == policyId && p.IsActive, ct))
                    Error("ACTIVITY_SLA", "Select an active SLA policy available to this tenant.");
                if (activity.Outcomes.Any(o => o.OutcomeKey.Equals("reject", StringComparison.OrdinalIgnoreCase)))
                {
                    if (string.IsNullOrWhiteSpace(config.RejectTargetNodeKey) || !version.Activities.Any(a => a.NodeKey == config.RejectTargetNodeKey && a.ActivityType is ActivityType.UserTask or ActivityType.MainActivity))
                        Error("ACTIVITY_REWORK", "Select a business activity as the rejection/rework destination.");
                    if (activity.Outcomes.Any(o => o.OutcomeKey.Equals("reject", StringComparison.OrdinalIgnoreCase) && !o.RequiresComment))
                        Error("REJECTION_COMMENT", "Reject must require a comment.");
                }
            }
            if (activity.ActivityType == ActivityType.MainActivity)
            {
                if (!activity.Outcomes.Any(o => o.OutcomeKey.Equals("approve", StringComparison.OrdinalIgnoreCase)))
                    Error("MAIN_APPROVAL", "Main activities require an Approve outcome.");
                var child = await ResolveChildAsync(config, ct);
                if (child is null) Error("MAIN_CHILD", "Select a published child workflow.");
                else
                {
                    var childSettings = JsonSerializer.Deserialize<WorkflowWorkspaceDefinition>(child.WorkspaceJson ?? "null", IntegrationJson.Options);
                    if (childSettings?.Kind != "Child") Error("MAIN_CHILD_KIND", "Main activities require a workflow published as Child.");
                    if (!await CheckTreeAsync(child, new HashSet<Guid> { version.WorkflowDefinitionId }, 1, ct))
                        Error("CHILD_RECURSION", "Child workflow references must be acyclic and at most 16 levels deep.");
                }
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in config.Events ?? [])
            {
                if (item is null) { Error("EVENT_CONFIG", "Configure each activity event."); continue; }
                if (string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 64 || !ids.Add(item.Id)) Error("EVENT_ID", "Every event needs a unique identifier of at most 64 characters within its activity.");
                if (item.Trigger is not ("OnEnter" or "OnApprove" or "OnReject" or "OnComment" or "OnComplete" or "OnFailure" or "OnSlaBreach")) Error("EVENT_TRIGGER", "Select a supported event trigger.");
                if (item.Kind is not ("Http" or "Soap" or "Sms" or "Email")) { Error("EVENT_KIND", "Select REST, SOAP, SMS or Email."); continue; }
                if (item.Configuration.ValueKind != JsonValueKind.Object) { Error("EVENT_CONFIG", "Configure the event request."); continue; }
                foreach (var message in IntegrationConfigurationRules.Validate(item.Kind == "Email" ? "Email" : "Http", item.DeliveryConfiguration())) Error("EVENT_CONFIG", message);
                if (!item.Configuration.TryGetProperty("connectionId", out var id) || id.ValueKind != JsonValueKind.String || !Guid.TryParse(id.GetString(), out var connectionId)
                    || !await db.IntegrationConnections.AnyAsync(c => c.Id == connectionId && c.Kind == (item.Kind == "Email" ? "Smtp" : "Http"), ct))
                    Error("EVENT_CONNECTION", "Select a compatible connection in this tenant.");
            }
        }
        return errors;
    }

    public async Task<Result> PreparePublicationAsync(WorkflowVersion version, CancellationToken ct)
    {
        var errors = await ValidateAsync(version, ct);
        if (errors.Count > 0) return Result.Failure(new Error(errors[0].Code, errors[0].Message));
        if (version.WorkspaceJson is null) return Result.Success();
        var pins = new Dictionary<string, Guid>();
        foreach (var activity in version.Activities.Where(a => a.ActivityType is ActivityType.MainActivity or ActivityType.CallActivity))
        {
            var child = await ResolveChildAsync(IntegrationJson.Read<BusinessActivityConfiguration>(activity.ConfigurationJson), ct);
            if (child is not null) pins[activity.NodeKey] = child.Id;
        }
        version.PinChildVersions(JsonSerializer.Serialize(pins));
        var settings = JsonSerializer.Deserialize<WorkflowWorkspaceDefinition>(version.WorkspaceJson, IntegrationJson.Options)!;
        if (settings.Kind == "Main")
        {
            var definition = await db.WorkflowDefinitions.SingleAsync(d => d.Id == version.WorkflowDefinitionId, ct);
            var screen = "workspace:" + definition.Id;
            if (!await db.WorkflowBindings.AnyAsync(b => b.ScreenKey == screen, ct))
            {
                var binding = WorkflowBinding.Create(definition.Id, definition.OrganizationId, "Standalone", "WorkflowRequest", "RequestSubmitted",
                    DateTime.UtcNow, "Managed workflow workspace binding", WorkflowBindingMode.Active, screenKey: screen);
                binding.Activate(DateTime.UtcNow); db.WorkflowBindings.Add(binding);
            }
        }
        return Result.Success();
    }

    private async Task<WorkflowVersion?> ResolveChildAsync(BusinessActivityConfiguration config, CancellationToken ct)
    {
        var definition = await db.WorkflowDefinitions.FirstOrDefaultAsync(d => d.DefinitionKey == config.DefinitionKey && d.IsActive, ct);
        if (definition is null) return null;
        var version = config.VersionId is Guid id ? await versions.GetByIdWithProjectionAsync(id, ct)
            : await versions.GetLatestPublishedWithProjectionAsync(definition.Id, ct);
        return version?.WorkflowDefinitionId == definition.Id && version.Status == WorkflowVersionStatus.Published ? version : null;
    }

    private async Task<bool> CheckTreeAsync(WorkflowVersion version, HashSet<Guid> ancestors, int depth, CancellationToken ct)
    {
        if (depth >= 16 || !ancestors.Add(version.WorkflowDefinitionId)) return false;
        var pins = JsonSerializer.Deserialize<Dictionary<string, Guid>>(version.PinnedChildVersionsJson ?? "{}")!;
        foreach (var activity in version.Activities.Where(a => a.ActivityType is ActivityType.MainActivity or ActivityType.CallActivity))
        {
            var config = IntegrationJson.Read<BusinessActivityConfiguration>(activity.ConfigurationJson);
            if (pins.TryGetValue(activity.NodeKey, out var pinned)) config.VersionId = pinned;
            var child = await ResolveChildAsync(config, ct);
            if (child is null || !await CheckTreeAsync(child, new HashSet<Guid>(ancestors), depth + 1, ct)) return false;
        }
        return true;
    }
}
