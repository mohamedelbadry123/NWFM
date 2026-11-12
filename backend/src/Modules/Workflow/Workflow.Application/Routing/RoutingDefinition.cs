using System.Text.Json;
using Workflow.Application.Integrations;
using Workflow.Application.Models;
using Workflow.Application.Workspace;
using Workflow.Domain.Entities;

namespace Workflow.Application.Routing;

public static class RoutingDefinition
{
    public static bool Enabled(string? workspace) => WorkspaceDesign.Configuration(workspace)["designerVersion"]?.GetValue<int>() == 4;
    public static RoutingConfiguration? Read(string? configuration) => WorkspaceDesign.Configuration(configuration)["routing"]?.Deserialize<RoutingConfiguration>(IntegrationJson.Options);
    public static RoutingGraph Graph(WorkflowVersion version)
    {
        var keys = version.Activities.ToDictionary(a => a.Id, a => a.NodeKey);
        return new(version.Activities.Where(a => WorkspaceDesign.Binding(a.ConfigurationJson) is null)
            .Select(a => new RoutingNode(a.NodeKey, a.Name, a.ActivityType.ToString(), Read(a.ConfigurationJson))).ToList(),
            version.Transitions.Select(e => new RoutingConnection(e.TransitionKey, keys.GetValueOrDefault(e.FromActivityDefinitionId, ""), keys.GetValueOrDefault(e.ToActivityDefinitionId, ""))).ToList());
    }
    public static RoutingGraph Graph(WorkflowXmlDocument doc) => new(
        doc.Activities.Where(a => WorkspaceDesign.Binding(a.ConfigurationJson) is null)
            .Select(a => new RoutingNode(a.NodeKey, a.Name, a.ActivityTypeName, Read(a.ConfigurationJson))).ToList(),
        doc.Transitions.Select(e => new RoutingConnection(e.Key, e.FromNodeKey, e.ToNodeKey)).ToList());
}
