using System.Text.Json;
using Workflow.Application.DTOs;
using Workflow.Application.Integrations;
using Workflow.Application.Mapping;
using Workflow.Domain.Entities;

namespace Workflow.Infrastructure.Services;

internal sealed class MappingPublicationValidator(IDesignerMappingCatalog catalogs)
{
    internal async Task<IReadOnlyList<WorkflowValidationIssueDto>> ValidateAsync(WorkflowVersion version, CancellationToken ct, MappingTreePin? expectedTree = null)
    {
        var errors = new List<WorkflowValidationIssueDto>();
        var typed = new List<ActivityDefinition>();
        foreach (var node in version.Activities)
        {
            try { using var document = JsonDocument.Parse(node.ConfigurationJson ?? "{}"); if (document.RootElement.TryGetProperty("typedMapping", out _)) typed.Add(node); }
            catch (JsonException) { errors.Add(new("MAPPING_INVALID", "Invalid node configuration JSON.", node.NodeKey)); }
        }
        if (typed.Count == 0) return errors;
        if (!Workflow.Application.Routing.RoutingDefinition.Enabled(version.WorkspaceJson))
            return [new("MAPPING_ROUTING_VERSION", "Typed mappings require the versioned designer routing runtime.")];
        foreach (var node in typed)
        {
            try
            {
                var config = JsonDocument.Parse(node.ConfigurationJson!).RootElement.GetProperty("typedMapping").Deserialize<DesignerMappingConfiguration>(IntegrationJson.Options)
                    ?? throw new FormatException("Typed configuration is required.");
                var trigger = Workflow.Application.Workspace.WorkspaceDesign.Binding(node.ConfigurationJson);
                if (trigger?.Trigger == "OnEnter" && Workflow.Application.Workspace.WorkspaceDesign.Configuration(node.ConfigurationJson)["required"]?.GetValue<bool>() != false
                    && config.Inputs.Select(i => i.Value).Concat(config.Outputs.Select(i => i.Value)).Concat(config.Actions.Select(i => i.Inputs))
                        .Concat(config.Selectors.Select(i => i.KeyValue)).SelectMany(MappingValidation.Nodes)
                    .Any(n => n.Source is { Kind: "FormField", SelectorId: null } s && s.WorkflowVersionId == version.Id && s.NodeKey == trigger.SourceNodeKey))
                    throw new FormatException("An OnEnter event cannot depend on forms that its own parent Step has not opened yet.");
                if (expectedTree is not null && config.TreeRoot is not null && config.TreeRoot != expectedTree) throw new FormatException("Child mapping pins a different owning main workflow version.");
                var tree = expectedTree ?? config.TreeRoot ?? new(version.WorkflowDefinitionId, version.Id);
                var result = await catalogs.GetAsync(tree.WorkflowDefinitionId, tree.WorkflowVersionId, ct);
                if (result.IsFailure) throw new FormatException(result.Error.Message);
                var sources = result.Value.Sources.ToList();
                var relatedTrees = new List<RelatedMappingTree>();
                var relatedChoices = config.Selectors.Count == 0 ? [] : await catalogs.RelatedAsync(ct);
                foreach (var selector in config.Selectors)
                {
                    if (!relatedChoices.Any(c => c.DefinitionId == selector.WorkflowDefinitionId && c.VersionId == selector.WorkflowVersionId)) throw new FormatException("Related source pin is unavailable or unauthorized.");
                    var related = await catalogs.GetAsync(selector.WorkflowDefinitionId, selector.WorkflowVersionId, ct);
                    if (related.IsFailure) throw new FormatException(related.Error.Message);
                    sources.AddRange(related.Value.Sources);
                    relatedTrees.Add(new(new(selector.WorkflowDefinitionId, selector.WorkflowVersionId), related.Value.TreeNodes ?? []));
                }
                var catalog = result.Value with { Sources = sources.DistinctBy(s => s.Reference).ToList(), RelatedTrees = relatedTrees };
                errors.AddRange(MappingValidation.Validate(config, catalog, new(version.WorkflowDefinitionId, version.Id, node.NodeKey)).Select(e => new WorkflowValidationIssueDto("MAPPING_INVALID", e, node.NodeKey)));
                if (config.StepBindings.Count > 0 && IntegrationJson.Read<Workflow.Application.Workspace.BusinessActivityConfiguration>(node.ConfigurationJson).FormSteps.Count == 0)
                    throw new FormatException("Typed Step mappings require pinned form steps.");
                if (config.Endpoint is not null)
                    errors.AddRange(IntegrationConfigurationRules.Validate("Http", node.ConfigurationJson!).Select(e => new WorkflowValidationIssueDto("MAPPING_HTTP_INVALID", e, node.NodeKey)));
            }
            catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or NullReferenceException)
            { errors.Add(new("MAPPING_INVALID", "Invalid typed mapping: " + ex.Message, node.NodeKey)); }
        }
        return errors;
    }
}
