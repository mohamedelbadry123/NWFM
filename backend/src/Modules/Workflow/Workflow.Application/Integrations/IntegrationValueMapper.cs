namespace Workflow.Application.Integrations;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

public static class IntegrationValueMapper
{
    private static readonly Regex Token = new(@"\{\{\s*([A-Za-z0-9_.-]+)\s*\}\}", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    public static string Render(string? value, IReadOnlyDictionary<string, JsonElement> variables)
        => Token.Replace(value ?? "", match => variables.TryGetValue(match.Groups[1].Value, out var v)
            ? Scalar(v) : throw new InvalidOperationException($"Variable '{match.Groups[1].Value}' is missing."));
    public static string Scalar(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
    public static string RenderJson(string value, IReadOnlyDictionary<string, JsonElement> variables)
    {
        JsonNode? Visit(JsonNode? node)
        {
            if (node is JsonObject obj) { foreach (var key in obj.Select(x => x.Key).ToArray()) obj[key] = Visit(obj[key]); }
            else if (node is JsonArray array) { for (var i = 0; i < array.Count; i++) array[i] = Visit(array[i]); }
            else if (node is JsonValue val && val.TryGetValue<string>(out var text))
            {
                var match = Token.Match(text);
                if (match.Success && match.Length == text.Length)
                    return variables.TryGetValue(match.Groups[1].Value, out var v) ? JsonNode.Parse(v.GetRawText())
                        : throw new InvalidOperationException($"Variable '{match.Groups[1].Value}' is missing.");
                return JsonValue.Create(Render(text, variables));
            }
            return node?.DeepClone();
        }
        return Visit(JsonNode.Parse(value))?.ToJsonString() ?? "null";
    }

    public static Dictionary<string, object?> Map(string json, Dictionary<string, string> mappings)
    {
        using var document = JsonDocument.Parse(json);
        var result = new Dictionary<string, object?>();
        foreach (var (name, path) in mappings)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("An output variable name is required.");
            var value = document.RootElement;
            var normalized = path.Trim();
            if (normalized.StartsWith("$.")) normalized = normalized[2..];
            if (normalized != "$" && normalized.Length > 0)
                foreach (var segment in normalized.Split('.'))
                {
                    if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty(segment, out var child)) value = child;
                    else if (value.ValueKind == JsonValueKind.Array && int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                        && index >= 0 && index < value.GetArrayLength()) value = value[index];
                    else throw new InvalidOperationException($"Response path '{path}' is missing.");
                }
            result[name] = value.Clone();
        }
        return result;
    }
}
