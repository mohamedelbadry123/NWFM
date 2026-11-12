using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Organization;
using Workflow.Application.Workspace;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

namespace Workflow.Infrastructure.Services;

internal sealed partial class WorkflowWorkspace
{
    public async Task<WorkspaceLibraryPage> LibraryAsync(WorkspaceLibraryQuery query, CancellationToken ct)
    {
        // Read metadata, never XML/designer payloads. Tenant filters apply to every query.
        var definitions = await db.WorkflowDefinitions.AsNoTracking().ToListAsync(ct);
        var versionRows = await db.WorkflowVersions.AsNoTracking().Select(v => new
        { v.Id, v.WorkflowDefinitionId, v.VersionNumber, v.Status, v.CreatedAt, v.UpdatedAt }).ToListAsync(ct);
        var latestIds = versionRows.GroupBy(v => v.WorkflowDefinitionId)
            .Select(g => g.OrderByDescending(v => v.VersionNumber).First().Id).ToArray();
        var settingsRows = await db.WorkflowVersions.AsNoTracking().Where(v => latestIds.Contains(v.Id))
            .Select(v => new { v.Id, v.WorkspaceJson }).ToListAsync(ct);
        var activities = await db.ActivityDefinitions.AsNoTracking().Where(a => latestIds.Contains(a.WorkflowVersionId))
            .Select(a => new { a.WorkflowVersionId, a.ActivityType, a.ConfigurationJson }).ToListAsync(ct);
        var settings = settingsRows.ToDictionary(v => v.Id, v => ReadLibrarySettings(v.WorkspaceJson));
        var byDefinition = versionRows.ToLookup(v => v.WorkflowDefinitionId);
        var byVersion = versionRows.ToDictionary(v => v.Id);
        var activityTypes = references is null ? [] : await references.ListAsync("field-activity-types", null, ct);
        var taskTypeRows = taskTypes is null ? [] : await taskTypes.ListAsync(ct);
        var items = new Dictionary<Guid, WorkspaceLibraryItem>();
        foreach (var definition in definitions)
        {
            var history = byDefinition[definition.Id].ToList();
            var latest = history.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
            var context = latest is null ? null : settings.GetValueOrDefault(latest.Id);
            var classification = context?.Kind == "Child"
                ? taskTypeRows.FirstOrDefault(t => t.Id == context.TaskTypeId)
                : activityTypes.FirstOrDefault(t => t.Id == context?.FieldActivityTypeId || context?.FieldActivityCode != null && t.Code == context.FieldActivityCode);
            var modified = history.Select(v => v.UpdatedAt > v.CreatedAt ? v.UpdatedAt : v.CreatedAt)
                .Append(definition.UpdatedAt).Append(definition.CreatedAt).Max();
            items.Add(definition.Id, new(definition.Id, definition.DefinitionKey, definition.Name, definition.NameAr,
                definition.Description, definition.DescriptionAr, definition.IsActive, definition.CreatedAt, modified,
                history.Count, latest?.Id, latest?.VersionNumber, latest?.Status.ToString() ?? "None",
                context?.Kind is "Main" or "Child" ? context.Kind : "Unclassified", context?.ParentDefinitionId,
                context?.ParentNodeKey, classification,
                activities.Count(a => a.WorkflowVersionId == latest?.Id && a.ActivityType is ActivityType.MainActivity or ActivityType.UserTask),
                LibraryScopes(context), context is { Kind: "Child", HasScopeSettings: false },
                context is null || context.HasLegacyConflict, []));
        }

        // Legacy children have no owner in their settings. Infer only an unambiguous link
        // from the latest parent drawing; shared children remain independently accessible.
        var referencesByChild = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var activity in activities.Where(a => a.ActivityType is ActivityType.MainActivity or ActivityType.CallActivity))
        {
            try
            {
                using var config = JsonDocument.Parse(activity.ConfigurationJson ?? "{}");
                Guid? childId = null;
                if (config.RootElement.TryGetProperty("versionId", out var id) && id.ValueKind == JsonValueKind.String
                    && Guid.TryParse(id.GetString(), out var versionId) && byVersion.TryGetValue(versionId, out var version))
                    childId = version.WorkflowDefinitionId;
                else if (config.RootElement.TryGetProperty("definitionKey", out var key) && key.ValueKind == JsonValueKind.String)
                    childId = definitions.FirstOrDefault(d => d.DefinitionKey == key.GetString())?.Id;
                if (childId is not Guid child || !items.ContainsKey(child)) continue;
                if (!referencesByChild.TryGetValue(child, out var parents)) referencesByChild[child] = parents = [];
                parents.Add(byVersion[activity.WorkflowVersionId].WorkflowDefinitionId);
            }
            catch (JsonException) { /* An invalid draft must not hide the rest of the library. */ }
            catch (InvalidOperationException) { /* A malformed configuration is shown by the designer. */ }
        }
        foreach (var item in items.Values.ToArray())
        {
            if (item.Kind != "Child" || item.ParentDefinitionId is not null) continue;
            var parents = referencesByChild.GetValueOrDefault(item.Id);
            items[item.Id] = item with
            {
                ParentDefinitionId = parents?.Count == 1 ? parents.First() : null,
                NeedsAttention = item.NeedsAttention || parents?.Count != 1
            };
        }
        // Broken owners and cycles must never make definitions disappear or recurse forever.
        foreach (var item in items.Values.ToArray())
        {
            var visited = new HashSet<Guid> { item.Id };
            var parent = item.ParentDefinitionId;
            while (parent is Guid id)
            {
                if (!visited.Add(id) || !items.TryGetValue(id, out var ancestor))
                {
                    items[item.Id] = item with { ParentDefinitionId = null, NeedsAttention = true };
                    break;
                }
                parent = ancestor.ParentDefinitionId;
            }
        }

        // Resolve scope names in batches so filters and rows use business labels, not just codes.
        var allScopes = items.Values.SelectMany(i => i.Scopes).DistinctBy(s => (s.Level, s.Code)).ToList();
        var namedScopes = new Dictionary<(string, string), WorkspaceLibraryScope>();
        foreach (var group in allScopes.GroupBy(s => s.Level))
        {
            var names = await directory.GetUnitNamesAsync(group.Key, group.Select(s => s.Code).ToArray(), ct);
            foreach (var scope in group)
            {
                var name = names.GetValueOrDefault(scope.Code);
                namedScopes[(scope.Level, scope.Code)] = scope with { NameEn = name?.NameEn, NameAr = name?.NameAr };
            }
        }
        foreach (var item in items.Values.ToArray())
            items[item.Id] = item with { Scopes = item.Scopes.Select(s => namedScopes[(s.Level, s.Code)]).ToList() };

        var children = items.Values.Where(i => i.ParentDefinitionId != null).ToLookup(i => i.ParentDefinitionId!.Value);
        var term = query.Search?.Trim();
        bool Matches(WorkspaceLibraryItem item) =>
            (query.Status == "All" || item.IsActive == (query.Status == "Active"))
            && (string.IsNullOrEmpty(query.Scope) || item.Scopes.Any(s => s.Level + ":" + s.Code == query.Scope))
            && (string.IsNullOrEmpty(term) || new[] { item.Name, item.NameAr, item.DefinitionKey, item.Description,
                item.DescriptionAr, item.Classification?.NameEn, item.Classification?.NameAr, item.Classification?.Code }
                .Concat(item.Scopes.SelectMany(s => new[] { s.Code, s.NameEn, s.NameAr }))
                .Any(s => s?.Contains(term, StringComparison.OrdinalIgnoreCase) == true));
        WorkspaceLibraryItem? Tree(WorkspaceLibraryItem item, bool includeFamily = false)
        {
            var matches = Matches(item);
            var descendants = children[item.Id].OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Id)
                .Select(child => Tree(child, includeFamily || matches)).OfType<WorkspaceLibraryItem>().ToList();
            var visible = query.Status == "All" || item.IsActive == (query.Status == "Active");
            return matches || includeFamily && visible || descendants.Count > 0 ? item with { Children = descendants } : null;
        }
        var roots = items.Values.Where(i => i.ParentDefinitionId == null).Select(i => Tree(i)).OfType<WorkspaceLibraryItem>();
        roots = query.Sort switch
        {
            "Name" => roots.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Id),
            "Created" => roots.OrderByDescending(i => i.CreatedAt).ThenBy(i => i.Id),
            _ => roots.OrderByDescending(i => i.UpdatedAt).ThenBy(i => i.Id),
        };
        var families = roots.ToList();
        var size = Math.Clamp(query.PageSize, 1, 50);
        var page = Math.Clamp(query.Page, 1, Math.Max(1, (int)Math.Ceiling(families.Count / (double)size)));
        return new(families.Skip((page - 1) * size).Take(size).ToList(), families.Count, items.Values.Count(Matches), page, size,
            namedScopes.Values.OrderBy(s => s.NameEn ?? s.Code).ToList());
    }

    private static WorkflowWorkspaceDefinition? ReadLibrarySettings(string? json)
    {
        try { return WorkflowWorkspaceDefinition.Read(json); }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static IReadOnlyList<WorkspaceLibraryScope> LibraryScopes(WorkflowWorkspaceDefinition? settings)
    {
        if (settings is null) return [];
        if (settings.OrganizationScopes is not null)
            return settings.OrganizationScopes.Where(s => s is { IsWellFormed: true })
                .Select(s => new WorkspaceLibraryScope(s.Level, s.Code, s.ClusterCode, s.CbuCode)).ToList();
        if (settings.Kind != "Main" || settings.ClusterCode is not string cluster) return [];
        var result = new List<WorkspaceLibraryScope>();
        if (settings.BranchCode is string branch) result.Add(new(OrgLevels.Branch, branch, cluster, settings.CbuCode));
        if (settings.OperationAreaCode is string area) result.Add(new(OrgLevels.OperationArea, area, cluster, settings.CbuCode));
        if (result.Count == 0) result.Add(settings.CbuCode is string cbu ? new(OrgLevels.Cbu, cbu, cluster, cbu) : new(OrgLevels.Cluster, cluster, cluster, null));
        return result;
    }
}
