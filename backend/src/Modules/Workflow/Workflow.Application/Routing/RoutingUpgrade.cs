using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Workflow.Application.Helpers;
using Workflow.Application.Integrations;
using Workflow.Application.Workspace;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

namespace Workflow.Application.Routing;

public sealed record RoutingUpgradeResult(string Xml, IReadOnlyList<RoutingDiagnostic> Diagnostics, int ConditionsAdded);
public static class RoutingUpgrade
{
    public static RoutingUpgradeResult Convert(WorkflowVersion version, IReadOnlyList<RoutingSource> sources, bool preserveRepairs = false)
    {
        var errors = new List<RoutingDiagnostic>();
        var settings = WorkspaceDesign.Configuration(version.WorkspaceJson);
        if (settings["designerVersion"]?.GetValue<int>() != 3)
            return new("", [new("ROUTING_UPGRADE_VERSION", "Only hierarchical designer version 3 drafts can be upgraded. Older versions retain their existing designer.")], 0);
        if (preserveRepairs && string.IsNullOrWhiteSpace(version.XmlContent))
        {
            XNamespace initialNamespace = "https://privora.io/workflow/v1";
            settings["designerVersion"] = 4;
            return new(new XElement(initialNamespace + "Workflow", new XAttribute("workspaceJson", settings.ToJsonString()),
                new XElement(initialNamespace + "Activities",
                    new XElement(initialNamespace + "Activity", new XAttribute("nodeKey", "start"), new XAttribute("type", "Start"), new XAttribute("name", "Start"), new XAttribute("positionX", 120), new XAttribute("positionY", 120)),
                    new XElement(initialNamespace + "Activity", new XAttribute("nodeKey", "end"), new XAttribute("type", "End"), new XAttribute("name", "End"), new XAttribute("positionX", 520), new XAttribute("positionY", 120))),
                new XElement(initialNamespace + "Transitions")).ToString(SaveOptions.DisableFormatting), [], 0);
        }
        using var reader = XmlReader.Create(new StringReader(version.XmlContent ?? ""), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 512 * 1024 });
        var doc = XDocument.Load(reader); var root = doc.Root!; var ns = root.Name.Namespace;
        var activities = root.Elements().First(e => e.Name.LocalName == "Activities");
        var transitions = root.Elements().First(e => e.Name.LocalName == "Transitions");
        var count = 0;
        foreach (var node in version.Activities.Where(a => a.ActivityType is not (ActivityType.Start or ActivityType.End) && WorkspaceDesign.Binding(a.ConfigurationJson) is null))
        {
            if (RoutingGraphRules.IsGate(node.ActivityType.ToString())) { errors.Add(new("ROUTING_UPGRADE_GATE", "Review existing gateway configuration before upgrading.", node.NodeKey)); continue; }
            var element = activities.Elements().First(e => e.Attribute("nodeKey")?.Value == node.NodeKey);
            var config = IntegrationJson.Read<BusinessActivityConfiguration>(node.ConfigurationJson);
            var gateKey = "condition_" + ConvertKey(node.NodeKey);
            var routing = new RoutingConfiguration { MatchMode = "First" };
            var repairs = new List<object>();
            var outgoing = version.Transitions.Where(t => t.FromActivityDefinitionId == node.Id).OrderBy(t => t.Priority).ThenBy(t => t.TransitionKey).ToList();
            if (preserveRepairs && config.FormSteps.Count <= 1 && config.FormSteps.All(f => f.Rules.Count == 0 && f.Otherwise.Kind == "Next")
                && string.IsNullOrWhiteSpace(config.RejectTargetNodeKey) && outgoing.Count <= 1 && outgoing.All(e => string.IsNullOrWhiteSpace(e.ConditionExpression))) continue;
            var originalElements = transitions.Elements().Where(e => e.Attribute("from")?.Value == node.NodeKey || e.Attribute("fromNodeKey")?.Value == node.NodeKey).ToList();
            // Both accepted XML attribute spellings are read by older definitions; exports use from/to.
            string AddEdge(string target, string suffix)
            {
                var key = gateKey + "_" + suffix;
                transitions.Add(new XElement(ns + "Transition", new XAttribute("key", key), new XAttribute("from", gateKey), new XAttribute("to", target), new XAttribute("priority", routing.Routes.Count)));
                return key;
            }
            RoutingRule Outcome(string target, string value, string suffix)
            {
                var key = node.NodeKey + ".$outcome";
                return new() { TransitionKey = AddEdge(target, suffix), When = new() { Conditions = [new() { Field = key, Operator = "equal", Value = JsonSerializer.SerializeToElement(value) }] } };
            }
            if (config.RejectTargetNodeKey is string reject && !string.IsNullOrWhiteSpace(reject)) routing.Routes.Add(Outcome(reject, "REJECT", "reject"));
            if (config.FormSteps.Count > 1) { errors.Add(new("ROUTING_UPGRADE_FORMS", "Upgrade requires one form per Step.", node.NodeKey)); continue; }
            var form = config.FormSteps.FirstOrDefault();
            foreach (var rule in form?.Rules ?? [])
            {
                var condition = JsonSerializer.Deserialize<FormConditionGroup>(JsonSerializer.Serialize(rule.When))!;
                Prefix(condition, node.NodeKey + ".");
                string? target = null;
                try { target = WorkflowFormRouting.ResolveDestination(config.FormSteps, form!, rule.Destination, version, node.NodeKey).NodeKey; }
                catch (FormatException ex) { errors.Add(new("ROUTING_UPGRADE_DESTINATION", ex.Message, node.NodeKey)); }
                if (target is null)
                {
                    errors.Add(new("ROUTING_UPGRADE_NEXT", "Give each legacy matching rule an explicit destination before upgrading.", node.NodeKey));
                    if (preserveRepairs)
                    {
                        var key = gateKey + "_repair_" + ConvertKey(rule.Id);
                        routing.Routes.Add(new() { TransitionKey = key, When = condition });
                        repairs.Add(new { transitionKey = key, message = "Choose where " + node.Name + " continues when this rule matches.", originalJson = JsonSerializer.Serialize(rule) });
                    }
                    continue;
                }
                routing.Routes.Add(new() { TransitionKey = AddEdge(target, "form_" + ConvertKey(rule.Id)), When = condition });
            }
            if (form?.Otherwise is { Kind: "Activity", Target: { } destination }) routing.FallbackTransitionKey = AddEdge(destination, "otherwise");
            else
            {
                foreach (var edge in outgoing.Where(e => !e.IsDefault && !string.IsNullOrWhiteSpace(e.ConditionExpression)))
                {
                    var match = Regex.Match(edge.ConditionExpression!, @"^\s*(OutcomeKey|LastActionTaken|outcome)\s*==\s*'([^']+)'\s*$", RegexOptions.CultureInvariant);
                    var target = version.Activities.Single(a => a.Id == edge.ToActivityDefinitionId).NodeKey;
                    if (match.Success) routing.Routes.Add(Outcome(target, match.Groups[2].Value, ConvertKey(edge.TransitionKey)));
                    else
                    {
                        errors.Add(new("ROUTING_UPGRADE_EXPRESSION", "Convert this arrow expression to typed rules before upgrading: " + edge.TransitionKey, node.NodeKey));
                        if (preserveRepairs)
                        {
                            var key = AddEdge(target, "repair_" + ConvertKey(edge.TransitionKey));
                            routing.Routes.Add(new() { TransitionKey = key });
                            repairs.Add(new { transitionKey = key, message = "Replace the preserved expression with a supported rule for " + node.Name + ".", originalJson = edge.ConditionExpression });
                        }
                    }
                }
                var fallback = outgoing.FirstOrDefault(e => e.IsDefault) ?? outgoing.FirstOrDefault(e => string.IsNullOrWhiteSpace(e.ConditionExpression));
                if (fallback is not null) routing.FallbackTransitionKey = AddEdge(version.Activities.Single(a => a.Id == fallback.ToActivityDefinitionId).NodeKey, "otherwise");
            }
            if (routing.Routes.Count == 0 && routing.FallbackTransitionKey is { } unconditional)
            { routing.Routes.Add(new() { TransitionKey = unconditional, Always = true }); routing.FallbackTransitionKey = null; }
            routing.Sources = sources.Where(s => s.NodeKey == node.NodeKey && Used(routing, s.Key)).ToList();
            originalElements.ForEach(e => e.Remove());
            transitions.Add(new XElement(ns + "Transition", new XAttribute("key", gateKey + "_entry"), new XAttribute("from", node.NodeKey), new XAttribute("to", gateKey), new XAttribute("isDefault", "true")));
            activities.Add(new XElement(ns + "Activity", new XAttribute("nodeKey", gateKey), new XAttribute("type", "ExclusiveGateway"),
                new XAttribute("name", node.Name + " routing"), new XAttribute("positionX", (node.PositionX ?? 0) + 260), new XAttribute("positionY", node.PositionY ?? 0),
                new XAttribute("configurationJson", JsonSerializer.Serialize(new { routing, authoring = new { version = 1, repairs } }, IntegrationJson.Options))));
            var cleaned = WorkspaceDesign.Configuration(node.ConfigurationJson); cleaned.Remove("rejectTargetNodeKey");
            if (cleaned["formSteps"] is JsonArray formSteps)
                foreach (var step in formSteps.OfType<JsonObject>()) { step["rules"] = new JsonArray(); step["otherwise"] = new JsonObject { ["kind"] = "Next" }; }
            element.SetAttributeValue("configurationJson", cleaned.ToJsonString()); count++;
        }
        settings["designerVersion"] = 4; root.SetAttributeValue("workspaceJson", settings.ToJsonString());
        return new(doc.ToString(SaveOptions.DisableFormatting), errors, count);
    }
    public static string PrepareDesignerJson(WorkflowVersion version, string xml)
    {
        JsonObject state;
        try { state = JsonNode.Parse(version.DesignerJson ?? "{}") as JsonObject ?? new(); }
        catch (JsonException) { state = new(); }
        var document = XDocument.Parse(xml);
        var root = document.Root!;
        var previousNodes = (state["nodes"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
        var previousEdges = (state["edges"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
        var nodes = new JsonArray(); var identifiers = new Dictionary<string, string>();
        foreach (var element in root.Elements().First(e => e.Name.LocalName == "Activities").Elements())
        {
            var key = element.Attribute("nodeKey")?.Value ?? "";
            var node = previousNodes.FirstOrDefault(n => n["nodeKey"]?.GetValue<string>() == key)?.DeepClone() as JsonObject ?? new();
            var id = node["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString(); identifiers[key] = id;
            node["id"] = id; node["nodeKey"] = key; node["type"] = element.Attribute("type")?.Value;
            node["name"] = element.Attribute("name")?.Value ?? key;
            node["nameAr"] = element.Attribute("nameAr")?.Value ?? node["nameAr"]?.GetValue<string>() ?? "";
            node["configurationJson"] = element.Attribute("configurationJson")?.Value ?? "{}";
            foreach (var property in new[] { ("x", "positionX"), ("y", "positionY") })
                if (node[property.Item1] is null)
                    node[property.Item1] = double.TryParse(element.Attribute(property.Item2)?.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 120;
            nodes.Add(node);
        }
        var edges = new JsonArray();
        foreach (var element in root.Elements().First(e => e.Name.LocalName == "Transitions").Elements())
        {
            var key = element.Attribute("key")?.Value ?? "";
            var from = element.Attribute("from")?.Value ?? element.Attribute("fromNodeKey")?.Value ?? "";
            var to = element.Attribute("to")?.Value ?? element.Attribute("toNodeKey")?.Value ?? "";
            if (!identifiers.TryGetValue(from, out var source) || !identifiers.TryGetValue(to, out var destination)) continue;
            var edge = previousEdges.FirstOrDefault(e => e["transitionKey"]?.GetValue<string>() == key)?.DeepClone() as JsonObject ?? new();
            edge["id"] ??= Guid.NewGuid().ToString(); edge["transitionKey"] = key;
            edge["fromNodeId"] = source; edge["toNodeId"] = destination;
            edge["conditionExpression"] = element.Attribute("condition")?.Value ?? ""; edge["outcomeKey"] = element.Attribute("outcomeKey")?.Value ?? "";
            edge["isDefault"] = element.Attribute("isDefault")?.Value == "true";
            edge["priority"] = int.TryParse(element.Attribute("priority")?.Value, out var priority) ? priority : 0;
            edges.Add(edge);
        }
        state["preparedFromLegacy"] = true;
        state["nodes"] = nodes; state["edges"] = edges;
        state["workspace"] = JsonNode.Parse(root.Attribute("workspaceJson")?.Value ?? "{}");
        return state.ToJsonString();
    }

    private static string ConvertKey(string value) => System.Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16].ToLowerInvariant();
    private static void Prefix(FormConditionGroup group, string prefix)
    { foreach (var condition in group.Conditions) condition.Field = prefix + condition.Field; foreach (var child in group.Groups) Prefix(child, prefix); }
    private static bool Used(RoutingConfiguration routing, string key)
    {
        bool Contains(FormConditionGroup group) => group.Conditions.Any(c => c.Field == key) || group.Groups.Any(Contains);
        return routing.Routes.Any(r => !r.Always && Contains(r.When));
    }
}
