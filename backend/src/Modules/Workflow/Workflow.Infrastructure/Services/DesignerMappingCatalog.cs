using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Constants;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Organization;
using NWFM.Shared.Results;
using Workflow.Application.Integrations;
using Workflow.Application.Mapping;
using Workflow.Application.Routing;
using Workflow.Application.Workspace;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;
using Workflow.Infrastructure.Persistence;

namespace Workflow.Infrastructure.Services;

internal sealed class DesignerMappingCatalog(WorkflowDbContext db, IWorkflowVersionRepository versions,
    ICurrentTenant tenant, ICurrentUser user, IWorkflowActorAccess actors, IFormGateway forms,
    IWorkflowTaskTypeCatalog taskTypes) : IDesignerMappingCatalog
{
    private async Task<WorkflowActorAccess?> AccessAsync(CancellationToken ct)
    {
        if (tenant.OrganizationId == Guid.Empty || !Guid.TryParse(user.Id, out var actorId)) return null;
        var access = await actors.GetAsync(actorId, ct);
        return access?.HasPermission(NwfmPolicies.ManageDefinitions) == true && access.HasPermission(NwfmPolicies.ViewInstances) ? access : null;
    }
    private static Error Missing => new("Mapping.Catalog.NotFound", "The workflow version is unavailable or outside your source-data access.");
    internal static bool Allowed(WorkflowWorkspaceDefinition? settings, OrgScopeSet scope, OrgHierarchy hierarchy,
        string? department = null, string? activity = null)
    {
        if (settings is null || settings.HasLegacyConflict || !settings.HasRequiredLocation) return false;
        if (!settings.HasScopeSettings) return settings.Location.IsCoveredBy(scope, department ?? settings.DepartmentCode, activity ?? settings.FieldActivityCode);
        return scope.Overlaps(OrgScopeSet.FromRows(settings.OrganizationScopes!.Select(s => new OrgScopeRow(s.Level, s.Code,
            string.IsNullOrEmpty(department ?? settings.DepartmentCode) ? [] : [department ?? settings.DepartmentCode!],
            string.IsNullOrEmpty(activity ?? settings.FieldActivityCode) ? [] : [activity ?? settings.FieldActivityCode!])), hierarchy));
    }
    public async Task<IReadOnlyList<MappingDefinitionChoice>> RelatedAsync(CancellationToken ct)
    {
        var access = await AccessAsync(ct); if (access is null) return [];
        var scope = access.Scope; var hierarchy = access.Hierarchy;
        var candidates = await (from v in db.WorkflowVersions.AsNoTracking()
                                join d in db.WorkflowDefinitions.AsNoTracking() on v.WorkflowDefinitionId equals d.Id
                                where d.OrganizationId == tenant.OrganizationId && d.IsActive && v.Status == WorkflowVersionStatus.Published
                                orderby d.Name, v.VersionNumber descending
                                select new { d.Id, d.Name, VersionId = v.Id, v.VersionNumber, v.WorkspaceJson }).Take(1000).ToListAsync(ct);
        var result = new List<MappingDefinitionChoice>();
        foreach (var candidate in candidates)
        {
            try { var settings = WorkflowWorkspaceDefinition.Read(candidate.WorkspaceJson); if (settings?.Kind == WorkflowWorkspaceDefinition.MainKind && Allowed(settings, scope, hierarchy)) result.Add(new(candidate.Id, candidate.VersionId, candidate.VersionNumber, candidate.Name)); }
            catch (JsonException) { /* Malformed drafts/metadata are not a source grant. */ }
        }
        return result;
    }
    public async Task<Result<MappingCatalog>> GetAsync(Guid definitionId, Guid versionId, CancellationToken ct)
    {
        var access = await AccessAsync(ct); if (access is null) return Result.Failure<MappingCatalog>(Missing);
        var scope = access.Scope; var hierarchy = access.Hierarchy;
        var sources = new List<SourceDescriptor>(); var targets = new List<CallableTargetDescriptor>();
        var diagnostics = new List<string>();
        var nodes = new List<CallableTarget>(); var dependencies = new List<MappingDependency>(); var visited = new HashSet<Guid>();
        async Task<WorkflowVersion?> Load(Guid definition, Guid version)
        {
            if (!await db.WorkflowDefinitions.AnyAsync(d => d.Id == definition && d.OrganizationId == tenant.OrganizationId, ct)) return null;
            var v = await versions.GetByIdWithProjectionAsync(version, ct);
            return v?.WorkflowDefinitionId == definition && Allowed(WorkflowWorkspaceDefinition.Read(v.WorkspaceJson), scope, hierarchy) ? v : null;
        }
        async Task Walk(WorkflowVersion version, List<string> breadcrumb, int depth)
        {
            if (depth > 8 || !visited.Add(version.Id) || visited.Count > 100) throw new FormatException("Workflow tree contains a cycle or exceeds catalog limits.");
            var settings = WorkflowWorkspaceDefinition.Read(version.WorkspaceJson)!;
            var pins = JsonSerializer.Deserialize<Dictionary<string, Guid>>(version.PinnedChildVersionsJson ?? "{}") ?? [];
            foreach (var variable in version.Variables)
            {
                var schema = variable.DataType switch { VariableDataType.Boolean => new MappingSchema("boolean"), VariableDataType.Integer => new("integer"), VariableDataType.Decimal => new("number"), VariableDataType.Json => new("unknown"), _ => new("string") };
                sources.Add(new(new(version.WorkflowDefinitionId, version.Id, "$input", "WorkflowInput", variable.VariableKey), variable.Name, [.. breadcrumb, "Inputs", variable.Name], schema, schema.Type == "unknown" ? "SchemaRequired" : "InvocationStart", variable.IsSensitive));
            }
            nodes.Add(new(version.WorkflowDefinitionId, version.Id, "$input"));
            foreach (var node in version.Activities)
            {
                var identity = new CallableTarget(version.WorkflowDefinitionId, version.Id, node.NodeKey); nodes.Add(identity);
                var path = new List<string>(breadcrumb) { node.Name };
                var config = WorkspaceDesign.Configuration(node.ConfigurationJson);
                var trigger = WorkspaceDesign.Binding(node.ConfigurationJson);
                if (trigger is not null)
                {
                    var parent = new CallableTarget(version.WorkflowDefinitionId, version.Id, trigger.SourceNodeKey);
                    if (trigger.Trigger == "OnEnter" && config["required"]?.GetValue<bool>() != false)
                        dependencies.Add(new(identity, parent, "Graph"));
                    else if (trigger.Trigger is "OnComplete" or "OnApprove" or "OnReject")
                        dependencies.Add(new(parent, identity, "Graph"));
                }
                var businessJson = (System.Text.Json.Nodes.JsonObject)config.DeepClone(); businessJson.Remove("typedMapping");
                var business = IntegrationJson.Read<BusinessActivityConfiguration>(businessJson.ToJsonString());
                if (node.ActivityType == ActivityType.MainActivity)
                {
                    var pin = pins.GetValueOrDefault(node.NodeKey, business.VersionId ?? Guid.Empty);
                    var childDefinition = await db.WorkflowVersions.Where(v => v.Id == pin).Select(v => v.WorkflowDefinitionId).SingleOrDefaultAsync(ct);
                    var child = pin == Guid.Empty ? null : await Load(childDefinition, pin);
                    if (child is not null && WorkflowWorkspaceDefinition.Read(child.WorkspaceJson) is { } childSettings
                        && childSettings.ParentDefinitionId == version.WorkflowDefinitionId && childSettings.ParentNodeKey == node.NodeKey && childSettings.TaskTypeId == business.TaskTypeId)
                        await Walk(child, path, depth + 1);
                }
                // Form/Task catalogs are global. An authorized workflow association, classification and data permission are all required.
                if (access.HasPermission(NwfmPolicies.ViewSubmissions) && business.TaskTypeId is Guid typeId
                    && Allowed(settings, scope, hierarchy, business.DepartmentCode, business.FieldActivityCode))
                {
                    var associated = await taskTypes.FormIdsAsync(typeId, ct);
                    foreach (var form in business.FormSteps.Where(f => associated.Contains(f.FormId) && f.VersionNo > 0))
                    {
                        var metadata = await forms.FindFieldActivityFormAsync(form.FormId, ct);
                        if (metadata is null || !metadata.VersionNos.Contains(form.VersionNo)
                            || !Allowed(settings, scope, hierarchy, metadata.DepartmentCode, metadata.FieldActivityCode)) continue;
                        if (await forms.GetVersionSchemaAsync(form.FormId, form.VersionNo, ct) is null) continue;
                        foreach (var field in await forms.GetFieldsAsync(form.FormId, form.VersionNo, ct))
                        {
                            var schema = FieldSchema(field.FieldType); if (schema is null) continue;
                            sources.Add(new(new(version.WorkflowDefinitionId, version.Id, node.NodeKey, "FormField", field.DataName,
                                FormStepId: form.Id, FormId: form.FormId, FormVersion: form.VersionNo), field.LabelEn ?? field.DataName,
                                [.. path, form.Name, field.LabelEn ?? field.DataName], schema, "SubmittedForm"));
                        }
                    }
                }
                DesignerMappingConfiguration? mapping = null;
                try
                {
                    mapping = config["typedMapping"]?.Deserialize<DesignerMappingConfiguration>(IntegrationJson.Options);
                    if (mapping is not null)
                    {
                        if (mapping.ContractVersion != 1 || mapping.Outputs is null || mapping.Actions is null || mapping.Inputs is null || mapping.StepBindings is null || mapping.Selectors is null
                            || mapping.Outputs.Count > 100 || mapping.Actions.Count > 20 || mapping.Inputs.Count > 100 || mapping.StepBindings.Count > 100 || mapping.Selectors.Count > 20)
                            throw new FormatException("Invalid mapping collections.");
                        foreach (var output in mapping.Outputs) MappingTypes.ValidateSchema(output.Schema);
                        if (mapping.InvocationInputs is not null) MappingTypes.ValidateSchema(mapping.InvocationInputs);
                        foreach (var expression in mapping.Inputs.Select(b => b.Value).Concat(mapping.StepBindings.Select(b => b.Value)).Concat(mapping.Outputs.Select(o => o.Value)).Concat(mapping.Actions.Select(a => a.Inputs)).Concat(mapping.Selectors.Select(s => s.KeyValue))) MappingEvaluator.ValidateTree(expression);
                    }
                }
                catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or NullReferenceException)
                { mapping = null; diagnostics.Add(node.NodeKey + ": repair typed mapping metadata; valid sources remain available."); }
                if (mapping is not null)
                {
                    foreach (var output in mapping.Outputs)
                    {
                        MappingTypes.ValidateSchema(output.Schema);
                        sources.Add(new(new(version.WorkflowDefinitionId, version.Id, node.NodeKey, "EventOutput", output.Id), output.Name,
                            [.. path, "Outputs", output.Name], output.Schema, "CompletedOutput", output.Sensitive));
                    }
                    if (mapping.InvocationInputs is not null)
                        sources.Add(new(new(version.WorkflowDefinitionId, version.Id, node.NodeKey, "InvocationInput", "$inputs"), "Invocation inputs", [.. path, "Invocation inputs"], mapping.InvocationInputs, "InvocationStart"));
                    dependencies.AddRange(mapping.Actions.Where(a => a.Mode == "Run").Select(a => new MappingDependency(identity, a.Target, "Invocation")));
                    foreach (var expression in mapping.Inputs.Select(b => b.Value).Concat(mapping.StepBindings.Select(b => b.Value)).Concat(mapping.Outputs.Select(o => o.Value)).Concat(mapping.Actions.Select(a => a.Inputs)).Concat(mapping.Selectors.Select(s => s.KeyValue)))
                        dependencies.AddRange(MappingValidation.Nodes(expression).Where(n => n.Source is { SelectorId: null, Kind: "EventOutput" or "RoutingOutput" or "FormField" } source
                            && (source.WorkflowVersionId != version.Id || source.NodeKey != node.NodeKey)).Select(n => new MappingDependency(identity, new(n.Source!.WorkflowDefinitionId, n.Source.WorkflowVersionId, n.Source.NodeKey), "Source")));
                }
                foreach (var output in config["routingOutputs"]?.Deserialize<List<RoutingOutput>>(IntegrationJson.Options) ?? [])
                {
                    var schema = FieldSchema(output.Type); if (schema is null) continue;
                    sources.Add(new(new(version.WorkflowDefinitionId, version.Id, node.NodeKey, "RoutingOutput", output.Key), output.Label,
                        [.. path, "Outputs", output.Label], schema, "CompletedOutput", output.Sensitive));
                }
                if (node.ActivityType == ActivityType.ServiceTask && node.ActionKey == "http.request")
                    targets.Add(new(identity, node.Name, path, mapping?.InvocationInputs ?? new("object", Properties: []), mapping?.Callable == true));
            }
            foreach (var edge in version.Transitions)
            {
                var from = version.Activities.Single(a => a.Id == edge.FromActivityDefinitionId);
                var to = version.Activities.Single(a => a.Id == edge.ToActivityDefinitionId);
                dependencies.Add(new(new(version.WorkflowDefinitionId, version.Id, from.NodeKey), new(version.WorkflowDefinitionId, version.Id, to.NodeKey), "Graph"));
            }
            if (sources.Count > 5000) throw new FormatException("Source catalog exceeds 5000 fields.");
        }
        try
        {
            var version = await Load(definitionId, versionId); if (version is null) return Result.Failure<MappingCatalog>(Missing);
            var name = await db.WorkflowDefinitions.Where(d => d.Id == definitionId).Select(d => d.Name).SingleAsync(ct);
            await Walk(version, [name], 0);
            return Result.Success(new MappingCatalog(sources, targets, nodes, dependencies, diagnostics));
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or NullReferenceException)
        { return Result.Failure<MappingCatalog>(new("Mapping.Catalog.Invalid", "Source metadata is invalid. Repair the pinned workflow configuration.")); }
    }
    internal static MappingSchema? FieldSchema(string type) => type switch
    {
        "numeric" => new("number", Nullable: true),
        "yes_no" => new("boolean", Nullable: true),
        "multiple_choice" => new("array", Nullable: true, Items: new("string")),
        "text" or "memo" or "barcode" or "single_choice" or "date" or "date_time" or "time" => new("string", Nullable: true),
        "geolocation" => new("object", Nullable: true, Properties: new() { ["lat"] = new("number", Required: true), ["lng"] = new("number", Required: true), ["address"] = new("string") }),
        "calendar_with_hours" => new("object", Nullable: true, Properties: new[] { "sat", "sun", "mon", "tue", "wed", "thu", "fri" }.ToDictionary(d => d,
            _ => new MappingSchema("object", Properties: new() { ["from"] = new("string", Required: true), ["to"] = new("string", Required: true) }))),
        "photo" or "video" or "audio" or "file" or "signature" => new("array", Nullable: true, Items: new("object", Properties: new()
        { ["fileId"] = new("string", Required: true), ["path"] = new("string"), ["name"] = new("string"), ["type"] = new("string"), ["size"] = new("integer") })),
        _ => null
    };
}
