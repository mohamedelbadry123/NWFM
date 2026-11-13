using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Workflow.Application.Mapping;

public static class MappingTypes
{
    public const int MaxBytes = 262144;
    public const int MaxArray = 1000;
    public static void Bound(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Undefined && Encoding.UTF8.GetByteCount(value.GetRawText()) > MaxBytes)
            throw new FormatException("JSON exceeds 256 KB.");
    }
    public static void ValidateJson(JsonElement value, int depth = 0)
    {
        if (depth > 16) throw new FormatException("JSON nesting exceeds depth 16.");
        if (depth == 0) Bound(value);
        if (value.ValueKind == JsonValueKind.Array)
        { if (value.GetArrayLength() > MaxArray) throw new FormatException("Array exceeds 1000 items."); foreach (var item in value.EnumerateArray()) ValidateJson(item, depth + 1); }
        if (value.ValueKind == JsonValueKind.Object)
        { var keys = new HashSet<string>(); foreach (var p in value.EnumerateObject()) { Name(p.Name); if (!keys.Add(p.Name)) throw new FormatException("Duplicate object key."); ValidateJson(p.Value, depth + 1); } }
        if (value.ValueKind == JsonValueKind.Number && !value.TryGetDecimal(out _)) throw new FormatException("Number exceeds decimal range.");
    }
    public static void ValidateSchema(MappingSchema schema)
    {
        var count = 0;
        void Walk(MappingSchema s, int depth)
        {
            if (s is null || depth > 16 || ++count > 500) throw new FormatException("Schema exceeds node/depth limits.");
            if (s.Type is not ("string" or "number" or "integer" or "boolean" or "object" or "array")) throw new FormatException("Unsupported schema type.");
            if (s.Type == "object" && s.Properties is null || s.Type != "object" && s.Properties is not null
                || s.Type == "array" && s.Items is null || s.Type != "array" && s.Items is not null)
                throw new FormatException("Object properties and array items must match the schema type.");
            if ((s.AdditionalProperties || s.AdditionalPropertySchema is not null) && s.Type != "object"
                || s.AdditionalPropertySchema is not null && !s.AdditionalProperties) throw new FormatException("Additional properties require an open object schema.");
            if (s.AdditionalPropertySchema is not null) Walk(s.AdditionalPropertySchema, depth + 1);
            foreach (var p in s.Properties ?? []) { Name(p.Key); Walk(p.Value, depth + 1); }
            if (s.Items is not null) Walk(s.Items, depth + 1);
            if (s.Default.ValueKind != JsonValueKind.Undefined) ValidateValue(s, s.Default);
        }
        Walk(schema, 0);
    }
    public static void Name(string name)
    { if (string.IsNullOrEmpty(name) || name.Length > 200 || name.Any(char.IsControl)) throw new FormatException("Invalid or excessive literal name."); }
    public static void ValidateValue(MappingSchema s, JsonElement value, int depth = 0)
    {
        if (depth > 16) throw new FormatException("Value nesting exceeds limit.");
        Bound(value);
        if (value.ValueKind == JsonValueKind.Undefined) { if (s.Required) throw new FormatException("Required value is missing."); return; }
        if (value.ValueKind == JsonValueKind.Null) { if (!s.Nullable) throw new FormatException("Null is not allowed."); return; }
        var valid = s.Type switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "number" => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var n) && decimal.Truncate(n) == n,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            _ => false
        };
        if (!valid) throw new FormatException("Value does not match type " + s.Type + ". Use an explicit cast.");
        if (s.Type == "object")
        {
            var properties = value.EnumerateObject().ToList();
            if (properties.Select(p => p.Name).Distinct().Count() != properties.Count) throw new FormatException("Duplicate object keys.");
            foreach (var p in properties)
            {
                Name(p.Name);
                if (s.Properties?.TryGetValue(p.Name, out var child) == true) ValidateValue(child, p.Value, depth + 1);
                else if (!s.AdditionalProperties) throw new FormatException("Unknown object property: " + p.Name);
                else if (s.AdditionalPropertySchema is not null) ValidateValue(s.AdditionalPropertySchema, p.Value, depth + 1);
                else ValidateJson(p.Value, depth + 1);
            }
            foreach (var p in s.Properties ?? []) if (!value.TryGetProperty(p.Key, out _) && p.Value.Required) throw new FormatException("Required property is missing: " + p.Key);
        }
        if (s.Type == "array")
        {
            if (value.GetArrayLength() > MaxArray) throw new FormatException("Array exceeds 1000 items.");
            foreach (var item in value.EnumerateArray()) ValidateValue(s.Items!, item, depth + 1);
        }
    }
    public static JsonElement ApplyDefaults(MappingSchema schema, JsonElement value, int depth = 0)
    {
        if (depth > 16) throw new FormatException("Default nesting exceeds limit.");
        if (value.ValueKind == JsonValueKind.Undefined) value = schema.Default;
        if (value.ValueKind == JsonValueKind.Object && schema.Type == "object")
        {
            var values = value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
            foreach (var p in schema.Properties ?? [])
            {
                var child = ApplyDefaults(p.Value, values.GetValueOrDefault(p.Key), depth + 1);
                if (child.ValueKind != JsonValueKind.Undefined) values[p.Key] = child;
            }
            if (schema.AdditionalPropertySchema is not null)
                foreach (var key in values.Keys.Where(k => !schema.Properties!.ContainsKey(k)).ToArray())
                    values[key] = ApplyDefaults(schema.AdditionalPropertySchema, values[key], depth + 1);
            return JsonSerializer.SerializeToElement(values);
        }
        if (value.ValueKind == JsonValueKind.Array && schema.Type == "array")
            return JsonSerializer.SerializeToElement(value.EnumerateArray().Select(v => ApplyDefaults(schema.Items!, v, depth + 1)).ToArray());
        return value;
    }
    public static string[] Pointer(string pointer)
    {
        if (pointer == "") return [];
        if (pointer is null || pointer.Length > 1000 || !pointer.StartsWith('/') || Regex.IsMatch(pointer, "~(?![01])")) throw new FormatException("Use a bounded RFC 6901 JSON pointer.");
        return pointer[1..].Split('/').Select(s => s.Replace("~1", "/").Replace("~0", "~")).ToArray();
    }
    public static JsonElement Select(JsonElement value, string pointer)
    {
        foreach (var segment in Pointer(pointer))
        {
            if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty(segment, out var property)) value = property;
            else if (value.ValueKind == JsonValueKind.Array && int.TryParse(segment, out var index) && index >= 0 && index < value.GetArrayLength() && index.ToString(System.Globalization.CultureInfo.InvariantCulture) == segment) value = value[index];
            else return default;
        }
        return value;
    }
    public static MappingSchema SelectSchema(MappingSchema schema, string pointer)
    {
        foreach (var segment in Pointer(pointer))
        {
            if (schema.Type == "object" && schema.Properties?.TryGetValue(segment, out var property) == true) schema = property;
            else if (schema.Type == "object" && schema.AdditionalPropertySchema is not null) schema = schema.AdditionalPropertySchema;
            else if (schema.Type == "array" && int.TryParse(segment, out var index) && index >= 0 && index.ToString(System.Globalization.CultureInfo.InvariantCulture) == segment) schema = schema.Items!;
            else throw new FormatException("Unknown schema path: " + pointer);
        }
        return schema;
    }
    public static bool Comparable(MappingSchema left, MappingSchema right) => Compatible(left, right) || Compatible(right, left);
    public static bool Compatible(MappingSchema source, MappingSchema target)
    {
        if (source.Type == "null") return target.Nullable;
        if (source.Nullable && !target.Nullable || source.Type != target.Type && !(source.Type == "integer" && target.Type == "number")) return false;
        if (source.Type == "array") return Compatible(source.Items!, target.Items!);
        if (source.Type != "object") return true;
        foreach (var p in source.Properties ?? [])
        {
            if (target.Properties?.TryGetValue(p.Key, out var declared) == true) { if (!Compatible(p.Value, declared)) return false; }
            else if (!target.AdditionalProperties || target.AdditionalPropertySchema is not null && !Compatible(p.Value, target.AdditionalPropertySchema)) return false;
        }
        if ((target.Properties ?? []).Any(p => p.Value.Required && p.Value.Default.ValueKind == JsonValueKind.Undefined
            && !(source.Properties?.TryGetValue(p.Key, out var s) == true && s.Required))) return false;
        if (!source.AdditionalProperties) return true;
        if (!target.AdditionalProperties) return false;
        if (target.AdditionalPropertySchema is not null && (source.AdditionalPropertySchema is null || !Compatible(source.AdditionalPropertySchema, target.AdditionalPropertySchema))) return false;
        return (target.Properties ?? []).Where(p => source.Properties?.ContainsKey(p.Key) != true)
            .All(p => source.AdditionalPropertySchema is not null && Compatible(source.AdditionalPropertySchema, p.Value));
    }
    public static MappingSchema Infer(JsonElement value, int depth = 0)
    {
        Bound(value);
        if (depth > 16) throw new FormatException("Sample nesting exceeds limit.");
        if (depth == 0) ValidateJson(value);
        MappingSchema inferred = value.ValueKind switch
        {
            JsonValueKind.String => new("string", Inferred: true),
            JsonValueKind.Number when value.TryGetDecimal(out var n) => new(decimal.Truncate(n) == n ? "integer" : "number", Inferred: true),
            JsonValueKind.True or JsonValueKind.False => new("boolean", Inferred: true),
            JsonValueKind.Object => new("object", Properties: value.EnumerateObject().ToDictionary(p => p.Name, p => Infer(p.Value, depth + 1)), Inferred: true),
            JsonValueKind.Array when value.GetArrayLength() is > 0 and <= MaxArray => InferArray(value, depth),
            _ => throw new FormatException("Null, empty arrays and unsupported numbers need an explicit manual schema.")
        };
        if (depth == 0) ValidateSchema(inferred);
        return inferred;
    }
    public static MappingSchema InferResponse(JsonElement value)
    {
        ValidateJson(value);
        void Check(JsonElement item)
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                if (!item.EnumerateObject().Any()) throw new FormatException("An empty response object needs an explicit manual schema.");
                foreach (var property in item.EnumerateObject()) Check(property.Value);
            }
            else if (item.ValueKind == JsonValueKind.Array) foreach (var child in item.EnumerateArray()) Check(child);
        }
        Check(value); return Infer(value);
    }
    private static MappingSchema InferArray(JsonElement value, int depth)
    {
        var item = Infer(value[0], depth + 1);
        if (item.Type == "integer" && value.EnumerateArray().All(v => v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out _))
            && value.EnumerateArray().Any(v => v.GetDecimal() != decimal.Truncate(v.GetDecimal()))) item = item with { Type = "number" };
        foreach (var v in value.EnumerateArray()) ValidateValue(item, v);
        return new("array", Items: item, Inferred: true);
    }
}
