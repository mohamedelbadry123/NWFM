using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using Workflow.Application.Integrations;

namespace Workflow.Application.Workspace;

public sealed record EventTriggerBinding(string SourceNodeKey, string Trigger);
public static class WorkspaceDesign
{
    public static JsonObject Configuration(string? json) => JsonNode.Parse(json ?? "{}") as JsonObject ?? new();
    public static bool IsSimplified(string? json)
    {
        try { return Configuration(json)["designerVersion"]?.GetValue<int>() == 2; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return false; }
    }
    public static EventTriggerBinding? Binding(string? json)
    {
        try { return Configuration(json)["triggerBinding"]?.Deserialize<EventTriggerBinding>(IntegrationJson.Options); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
    }
    // Conversion is deterministic and only runs on editable simplified drafts.
    public static string NormalizeDraft(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 512 * 1024 });
        var doc = XDocument.Load(reader);
        if (!IsSimplified(doc.Root?.Attribute("workspaceJson")?.Value)) return xml;
        var activities = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Activities");
        if (activities is null) return xml;
        foreach (var activity in activities.Elements().ToArray())
        {
            var config = Configuration(activity.Attribute("configurationJson")?.Value);
            if (config["events"] is not JsonArray events || events.Count == 0) continue;
            var remaining = new JsonArray();
            foreach (var item in events)
            {
                if (item is not JsonObject ev || ev["kind"]?.GetValue<string>() is not ("Http" or "Soap" or "Sms" or "Email"))
                { remaining.Add(item?.DeepClone()); continue; }
                var source = activity.Attribute("nodeKey")!.Value;
                var id = ev["id"]?.GetValue<string>() ?? throw new JsonException("An existing event is missing its identifier.");
                var key = "event_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source + ":" + id)))[..24].ToLowerInvariant();
                if (activities.Elements().Any(e => e.Attribute("nodeKey")?.Value == key)) throw new JsonException("Converted event node already exists.");
                var delivery = ev["configuration"]?.DeepClone() as JsonObject ?? new();
                var kind = ev["kind"]!.GetValue<string>();
                delivery["protocol"] = kind == "Http" ? "Rest" : kind;
                delivery["required"] = ev["required"]?.DeepClone() ?? JsonValue.Create(kind is "Http" or "Soap");
                delivery["triggerBinding"] = new JsonObject { ["sourceNodeKey"] = source, ["trigger"] = ev["trigger"]?.DeepClone() };
                if (kind == "Email") { delivery["channels"] = "Email"; delivery["failurePolicy"] = "Retry"; }
                activities.Add(new XElement(activity.Name, new XAttribute("nodeKey", key), new XAttribute("type", kind == "Email" ? "NotificationTask" : "ServiceTask"),
                    new XAttribute("name", ev["name"]?.GetValue<string>() ?? kind),
                    kind == "Email" ? null : new XAttribute("actionKey", "http.request"),
                    new XAttribute("configurationJson", delivery.ToJsonString()), new XAttribute("positionX", 500), new XAttribute("positionY", 300 + activities.Elements().Count() * 110)));
            }
            config["events"] = remaining;
            activity.SetAttributeValue("configurationJson", config.ToJsonString());
        }
        return doc.ToString();
    }
}
