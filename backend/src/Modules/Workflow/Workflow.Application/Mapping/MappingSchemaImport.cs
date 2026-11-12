using System.Text;
using System.Text.Json;

namespace Workflow.Application.Mapping;

/// <summary>Local OpenAPI 3.0 JSON only. Never fetches URLs. Unsupported semantics are rejected, not silently approximated.</summary>
public static class MappingSchemaImport
{
    public static EndpointDefinition Import(SchemaImportRequest input)
    {
        if (Encoding.UTF8.GetByteCount(input.Document) > MappingTypes.MaxBytes) throw new FormatException("OpenAPI document exceeds 256 KB.");
        using var document = JsonDocument.Parse(input.Document, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        void UniqueKeys(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                { if (!names.Add(property.Name)) throw new FormatException("Duplicate OpenAPI member: " + property.Name); UniqueKeys(property.Value); }
            }
            else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) UniqueKeys(item);
        }
        UniqueKeys(root);
        if (!root.TryGetProperty("openapi", out var version) || !version.GetString()!.StartsWith("3.0.", StringComparison.Ordinal)) throw new FormatException("Import supports OpenAPI 3.0 JSON; use manual schema for other formats.");
        var count = 0;
        JsonElement Resolve(JsonElement element, HashSet<string> seen)
        {
            while (element.TryGetProperty("$ref", out var reference))
            {
                if (++count > 500 || element.EnumerateObject().Count() != 1) throw new FormatException("Reference limit or unsupported reference siblings.");
                var pointer = reference.GetString() ?? "";
                if (!pointer.StartsWith("#/", StringComparison.Ordinal) || !seen.Add(pointer)) throw new FormatException("Only acyclic internal references are supported.");
                element = MappingTypes.Select(root, pointer[1..]);
                if (element.ValueKind != JsonValueKind.Object) throw new FormatException("Unresolved schema reference.");
            }
            return element;
        }
        MappingSchema Schema(JsonElement element, bool required, HashSet<string> seen, int depth = 0)
        {
            if (++count > 500 || depth > 16) throw new FormatException("Schema/reference expansion limit exceeded.");
            element = Resolve(element, seen);
            var allowed = new[] { "type", "properties", "items", "required", "nullable", "default", "title", "description", "example", "additionalProperties" };
            foreach (var property in element.EnumerateObject()) if (!allowed.Contains(property.Name)) throw new FormatException("Unsupported schema keyword: " + property.Name);
            var hasExtra = element.TryGetProperty("additionalProperties", out var extra);
            if (hasExtra && extra.ValueKind is not (JsonValueKind.True or JsonValueKind.False or JsonValueKind.Object)) throw new FormatException("additionalProperties must be a boolean or schema.");
            if (!element.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) throw new FormatException("An explicit schema type is required.");
            var name = type.GetString()!;
            if (hasExtra && name != "object") throw new FormatException("additionalProperties belongs only to objects.");
            var requiredNames = element.TryGetProperty("required", out var req) ? req.EnumerateArray().Select(v => v.GetString() ?? "").ToList() : [];
            if (requiredNames.Distinct().Count() != requiredNames.Count) throw new FormatException("Duplicate required names.");
            Dictionary<string, MappingSchema>? properties = null;
            if (name == "object")
            {
                properties = element.TryGetProperty("properties", out var props) ? props.EnumerateObject().ToDictionary(p => p.Name, p => Schema(p.Value, requiredNames.Contains(p.Name), new(seen), depth + 1)) : [];
                if (requiredNames.Any(n => !properties.ContainsKey(n))) throw new FormatException("Required field has no schema.");
            }
            else if (requiredNames.Count > 0 || element.TryGetProperty("properties", out _)) throw new FormatException("Properties/required belong only to objects.");
            var items = element.TryGetProperty("items", out var item) ? Schema(item, false, new(seen), depth + 1) : null;
            return new(name, required, element.TryGetProperty("nullable", out var nullable) && nullable.GetBoolean(), properties, items,
                element.TryGetProperty("default", out var fallback) ? fallback.Clone() : default,
                AdditionalProperties: name == "object" && (!hasExtra || extra.ValueKind != JsonValueKind.False),
                AdditionalPropertySchema: hasExtra && extra.ValueKind == JsonValueKind.Object ? Schema(extra, false, new(seen), depth + 1) : null);
        }
        var path = root.GetProperty("paths").GetProperty(input.Path);
        if (path.TryGetProperty("$ref", out _)) throw new FormatException("Path references are unsupported; inline the selected path.");
        var operation = path.GetProperty(input.Method.ToLowerInvariant());
        if (operation.TryGetProperty("callbacks", out _)) throw new FormatException("Callbacks are unsupported.");
        var parameters = new List<EndpointParameter>();
        void Parameters(JsonElement parent)
        {
            if (!parent.TryGetProperty("parameters", out var list)) return;
            var seen = new HashSet<(string, string)>();
            foreach (var entry in list.EnumerateArray())
            {
                var parameter = Resolve(entry, []);
                var location = parameter.GetProperty("in").GetString()!;
                if (parameter.TryGetProperty("style", out var style) && style.GetString() != (location == "query" ? "form" : "simple")
                    || parameter.TryGetProperty("explode", out var explode) && explode.ValueKind != JsonValueKind.False && location != "query"
                    || parameter.TryGetProperty("allowReserved", out var reserved) && reserved.ValueKind != JsonValueKind.False
                    || parameter.TryGetProperty("content", out _)) throw new FormatException("Unsupported parameter serialization.");
                var parameterName = parameter.GetProperty("name").GetString()!;
                if (!seen.Add((location, parameterName))) throw new FormatException("Duplicate parameter in the same list.");
                parameters.RemoveAll(p => p.Location == location && p.Name == parameterName);
                parameters.Add(new(location, parameterName, Schema(parameter.GetProperty("schema"), parameter.TryGetProperty("required", out var required) && required.GetBoolean(), [])));
            }
        }
        Parameters(path); Parameters(operation);
        MappingSchema? body = null;
        if (operation.TryGetProperty("requestBody", out var request))
        {
            request = Resolve(request, []);
            var content = request.GetProperty("content");
            if (content.EnumerateObject().Count() != 1 || !content.TryGetProperty("application/json", out var json)) throw new FormatException("Typed mode requires application/json request bodies.");
            body = Schema(json.GetProperty("schema"), request.TryGetProperty("required", out var required) && required.GetBoolean(), []);
        }
        MappingSchema? response = null;
        foreach (var status in operation.GetProperty("responses").EnumerateObject().Where(p => p.Name.StartsWith('2')))
        {
            var entry = Resolve(status.Value, []);
            if (!entry.TryGetProperty("content", out var content)) continue;
            if (content.EnumerateObject().Count() != 1 || !content.TryGetProperty("application/json", out var json)) throw new FormatException("Selected success responses must use JSON.");
            var candidate = Schema(json.GetProperty("schema"), false, []);
            if (response is not null && (!MappingTypes.Compatible(candidate, response) || !MappingTypes.Compatible(response, candidate))) throw new FormatException("Success responses differ; select an explicit manual response contract.");
            response = candidate;
        }
        var endpoint = new EndpointDefinition(input.Method.ToUpperInvariant(), input.Path, parameters, body, response);
        MappingValidation.Endpoint(endpoint); return endpoint;
    }
}
