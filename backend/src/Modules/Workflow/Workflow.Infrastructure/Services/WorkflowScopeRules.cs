using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Organization;
using Workflow.Application.Integrations;
using Workflow.Application.Workspace;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;
using Workflow.Infrastructure.Persistence;

namespace Workflow.Infrastructure.Services;

/// <summary>Finds locations supported by the whole pinned child tree before execution creates any work.</summary>
internal static class WorkflowScopeRules
{
    public static async Task<IReadOnlyList<WorkflowOrganizationScope>> StartScopesAsync(
        WorkflowVersion version, WorkflowDbContext db, IWorkflowVersionRepository versions, CancellationToken ct)
    {
        var settings = WorkflowWorkspaceDefinition.Read(version.WorkspaceJson);
        if (settings is not { HasScopeSettings: true, HasRequiredLocation: true, HasLegacyConflict: false }) return [];
        var scopes = settings.OrganizationScopes!.ToList();
        var children = await DescendantsAsync(version, db, versions, new HashSet<Guid>(), 0, ct);
        if (children is null) return [];
        foreach (var child in children.Where(c => c.HasScopeSettings))
        {
            if (!child.HasRequiredLocation || child.HasLegacyConflict) return [];
            scopes = scopes.SelectMany(parent => child.OrganizationScopes!.SelectMany(nested =>
                parent.Contains(nested.Location) ? new[] { nested } : nested.Contains(parent.Location) ? new[] { parent } : []))
                .Distinct().ToList();
        }
        return scopes;
    }

    public static async Task<bool> AllowsTreeAsync(WorkflowVersion version, OrgLocation location,
        WorkflowDbContext db, IWorkflowVersionRepository versions, CancellationToken ct)
    {
        var settings = WorkflowWorkspaceDefinition.Read(version.WorkspaceJson);
        if (settings is null || !settings.AllowsLocation(location)) return false;
        var descendants = await DescendantsAsync(version, db, versions, new HashSet<Guid>(), 0, ct);
        return descendants is not null && descendants.All(c => !c.HasScopeSettings || c.AllowsLocation(location));
    }

    private static async Task<List<WorkflowWorkspaceDefinition>?> DescendantsAsync(WorkflowVersion version,
        WorkflowDbContext db, IWorkflowVersionRepository versions, HashSet<Guid> ancestors, int depth, CancellationToken ct)
    {
        if (depth >= 16 || !ancestors.Add(version.WorkflowDefinitionId)) return null;
        var result = new List<WorkflowWorkspaceDefinition>();
        var pins = JsonSerializer.Deserialize<Dictionary<string, Guid>>(version.PinnedChildVersionsJson ?? "{}")!;
        foreach (var activity in version.Activities.Where(a => a.ActivityType is ActivityType.MainActivity or ActivityType.CallActivity))
        {
            var config = IntegrationJson.Read<BusinessActivityConfiguration>(activity.ConfigurationJson);
            WorkflowVersion? child;
            if (pins.TryGetValue(activity.NodeKey, out var id) || config.VersionId is Guid)
                child = await versions.GetByIdWithProjectionAsync(pins.GetValueOrDefault(activity.NodeKey, config.VersionId ?? Guid.Empty), ct);
            else
            {
                var definition = await db.WorkflowDefinitions.AsNoTracking().FirstOrDefaultAsync(d => d.DefinitionKey == config.DefinitionKey && d.IsActive, ct);
                child = definition is null ? null : await versions.GetLatestPublishedWithProjectionAsync(definition.Id, ct);
            }
            if (child is null) return null;
            if (WorkflowWorkspaceDefinition.Read(child.WorkspaceJson) is { } settings) result.Add(settings);
            var descendants = await DescendantsAsync(child, db, versions, new HashSet<Guid>(ancestors), depth + 1, ct);
            if (descendants is null) return null;
            result.AddRange(descendants);
        }
        return result;
    }
}
