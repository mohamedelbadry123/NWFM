using System.Globalization;
using System.Text.Json;

namespace Workflow.Application.Mapping;

/// <summary>Authoritative pure mapper. Inputs are snapshots, never database lookups or executable text.</summary>
public static class MappingEvaluator
{
    private sealed class MissingValueException(string message) : FormatException(message);
    public static JsonElement Evaluate(TransformNode expression, IReadOnlyList<MappingSample> samples, Action<SourceReference>? observed = null)
    {
        ValidateTree(expression);
        if (samples.Count > 500 || samples.Select(s => s.Source).Distinct().Count() != samples.Count) throw new FormatException("Duplicate or excessive samples.");
        foreach (var sample in samples) MappingTypes.ValidateJson(sample.Value);
        var work = 0;
        JsonElement Run(TransformNode n, JsonElement item = default)
        {
            if (++work > 10000) throw new FormatException("Transform work limit exceeded.");
            var args = n.Args ?? [];
            JsonElement Arg(int i) => Run(args[i], item);
            JsonElement result;
            switch (n.Op)
            {
                case "literal": result = n.Value; if (n.LiteralSchema is not null) MappingTypes.ValidateValue(n.LiteralSchema, result); break;
                case "source":
                    var reference = n.Source!;
                    var sample = samples.SingleOrDefault(s => s.Source == reference with { JsonPointer = "" });
                    result = MappingTypes.Select(sample?.Value ?? default, reference.JsonPointer);
                    if (sample is not null) observed?.Invoke(reference with { JsonPointer = "" });
                    if (result.ValueKind == JsonValueKind.Undefined && n.Missing?.Mode == "Fallback") result = n.Missing.Fallback;
                    if (result.ValueKind == JsonValueKind.Undefined) throw new MissingValueException("Source is missing or unavailable: " + reference.FieldKey);
                    break;
                case "item": result = MappingTypes.Select(item, n.JsonPointer); if (result.ValueKind == JsonValueKind.Undefined) throw new MissingValueException("Array item property is missing."); break;
                case "object": result = JsonSerializer.SerializeToElement(n.Properties!.ToDictionary(p => p.Key, p => Run(p.Value, item))); break;
                case "concat": result = JsonSerializer.SerializeToElement(string.Concat(args.Select(a => Text(Run(a, item))))); break;
                case "add": result = JsonSerializer.SerializeToElement(checked(Number(Arg(0)) + Number(Arg(1)))); break;
                case "subtract": result = JsonSerializer.SerializeToElement(checked(Number(Arg(0)) - Number(Arg(1)))); break;
                case "multiply": result = JsonSerializer.SerializeToElement(checked(Number(Arg(0)) * Number(Arg(1)))); break;
                case "divide": var divisor = Number(Arg(1)); if (divisor == 0) throw new FormatException("Division by zero."); result = JsonSerializer.SerializeToElement(Number(Arg(0)) / divisor); break;
                case "text": result = JsonSerializer.SerializeToElement(ScalarText(Arg(0))); break;
                case "number": var number = Arg(0); result = JsonSerializer.SerializeToElement(number.ValueKind == JsonValueKind.Number ? Number(number) : decimal.TryParse(Text(number), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : throw new FormatException("Invalid number cast.")); break;
                case "integer": var integer = Arg(0); var whole = integer.ValueKind == JsonValueKind.Number ? Number(integer) : decimal.TryParse(Text(integer), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedInteger) ? parsedInteger : throw new FormatException("Invalid integer cast."); if (decimal.Truncate(whole) != whole) throw new FormatException("Integer cast cannot discard a fractional part."); result = JsonSerializer.SerializeToElement(whole); break;
                case "boolean": var boolean = Arg(0); result = boolean.ValueKind is JsonValueKind.True or JsonValueKind.False ? boolean : Text(boolean) switch { "true" => JsonSerializer.SerializeToElement(true), "false" => JsonSerializer.SerializeToElement(false), _ => throw new FormatException("Invalid boolean cast.") }; break;
                case "coalesce":
                    result = default;
                    foreach (var a in args) { try { result = Run(a, item); } catch (MissingValueException) { continue; } if (result.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)) break; }
                    if (result.ValueKind == JsonValueKind.Undefined) throw new MissingValueException("All coalesce values are missing.");
                    break;
                case "map":
                case "filter":
                    var array = Array(Arg(0)); var output = new List<JsonElement>();
                    foreach (var element in array) { var v = Run(args[1], element); if (n.Op == "map") output.Add(v); else if (Boolean(v)) output.Add(element); }
                    result = JsonSerializer.SerializeToElement(output); break;
                case "count": result = JsonSerializer.SerializeToElement(Array(Arg(0)).Count); break;
                case "sum": result = JsonSerializer.SerializeToElement(Array(Arg(0)).Aggregate(0m, (sum, v) => checked(sum + Number(v)))); break;
                case "equal": result = JsonSerializer.SerializeToElement(JsonElement.DeepEquals(Arg(0), Arg(1))); break;
                case "greater": result = JsonSerializer.SerializeToElement(Number(Arg(0)) > Number(Arg(1))); break;
                default: throw new FormatException("Unsupported transform.");
            }
            MappingTypes.ValidateJson(result);
            return result;
        }
        try { return Run(expression); }
        catch (OverflowException) { throw new FormatException("Decimal arithmetic overflow."); }
    }
    public static void ValidateTree(TransformNode expression)
    {
        var nodes = 0;
        void Walk(TransformNode n, int depth, bool item)
        {
            if (n is null || depth > 16 || ++nodes > 200) throw new FormatException("Transform exceeds 200 nodes or depth 16.");
            var count = n.Args?.Count ?? 0;
            var arity = n.Op switch { "literal" or "source" or "item" or "object" => 0, "text" or "number" or "integer" or "boolean" or "count" or "sum" => 1, "add" or "subtract" or "multiply" or "divide" or "map" or "filter" or "equal" or "greater" => 2, "concat" or "coalesce" => count is >= 1 and <= 100 ? count : -1, _ => -1 };
            if (arity < 0 || count != arity || n.Op != "object" && n.Properties is not null || n.Op != "source" && (n.Source is not null || n.Missing is not null)
                || n.Op != "literal" && (n.Value.ValueKind != JsonValueKind.Undefined || n.LiteralSchema is not null) || n.Op != "item" && n.JsonPointer != "") throw new FormatException("Invalid transform operation or arguments.");
            if (n.Op == "literal") { if (n.Value.ValueKind == JsonValueKind.Undefined) throw new FormatException("Literal value is missing."); MappingTypes.Bound(n.Value); }
            if (n.LiteralSchema is not null) { MappingTypes.ValidateSchema(n.LiteralSchema); MappingTypes.ValidateValue(n.LiteralSchema, n.Value); }
            if (n.Op == "source") { if (n.Source is null) throw new FormatException("Source reference required."); MappingTypes.Pointer(n.Source.JsonPointer); MappingValidation.Policy(n.Missing); }
            if (n.Op == "item") { if (!item) throw new FormatException("Item is only available inside map/filter."); MappingTypes.Pointer(n.JsonPointer); }
            if (n.Op == "object" && n.Properties is null) throw new FormatException("Object properties required.");
            foreach (var p in n.Properties ?? []) { MappingTypes.Name(p.Key); Walk(p.Value, depth + 1, item); }
            for (var i = 0; i < count; i++) Walk(n.Args![i], depth + 1, item || (n.Op is "map" or "filter" && i == 1));
        }
        Walk(expression, 0, false);
    }
    internal static decimal Number(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var n) ? n : throw new FormatException("Expected a decimal number.");
    internal static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new FormatException("Expected text; use an explicit cast.");
    internal static bool Boolean(JsonElement value) => value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw new FormatException("Expected a boolean predicate.");
    internal static string ScalarText(JsonElement value) => value.ValueKind switch { JsonValueKind.String => value.GetString()!, JsonValueKind.Number => Number(value).ToString(CultureInfo.InvariantCulture), JsonValueKind.True => "true", JsonValueKind.False => "false", _ => throw new FormatException("Expected a non-null scalar.") };
    private static List<JsonElement> Array(JsonElement value) => value.ValueKind == JsonValueKind.Array && value.GetArrayLength() <= MappingTypes.MaxArray ? value.EnumerateArray().ToList() : throw new FormatException("Expected an array of at most 1000 items.");
}
