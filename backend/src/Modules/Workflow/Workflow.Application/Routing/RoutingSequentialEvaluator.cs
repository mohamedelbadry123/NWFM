using System.Text.Json;
using Workflow.Application.Helpers;
using Workflow.Application.Workspace;

namespace Workflow.Application.Routing;

/// <summary>Version 2: order is the persisted Routes array; TransitionKey remains the stable identity.</summary>
internal static class RoutingSequentialEvaluator
{
    internal static bool SupportedType(string type) => type is "numeric" or "yes_no" or "multiple_choice" or "single_choice" or "text" or "memo" or "barcode" or "date" or "date_time" or "time";
    internal static RoutingDecision Evaluate(RoutingConfiguration config, IReadOnlyDictionary<string, object?> values)
    {
        // Publication validates all definitions. Runtime deliberately validates only visited definitions.
        if (config.MatchMode != "First" || config.Routes is null || config.Sources is null
            || config.Routes.Count > 100 || config.Sources.Count > 200)
            return new([], [], "Invalid ordered routing configuration.");
        var trace = new List<RoutingRuleTrace>();
        RoutingDecision Finish(string? match, string? error = null)
        {
            trace.AddRange(config.Routes.Skip(trace.Count).Select(r => new RoutingRuleTrace(r?.TransitionKey ?? "", "Skipped")));
            return new(match is not null ? [match] : error is null && config.FallbackTransitionKey is { Length: > 0 } fallback ? [fallback] : [],
                match is null ? [] : [match], error ?? (match is null && string.IsNullOrWhiteSpace(config.FallbackTransitionKey) ? "No condition matched. This route is stopped for attention." : null),
                match is null && error is null && !string.IsNullOrWhiteSpace(config.FallbackTransitionKey))
            { Rules = trace };
        }
        foreach (var rule in config.Routes)
        {
            try
            {
                if (rule is null || string.IsNullOrWhiteSpace(rule.TransitionKey) || trace.Any(t => t.TransitionKey == rule.TransitionKey)) throw new FormatException("Visited route has no unique transition identity.");
                var names = new HashSet<string>(StringComparer.Ordinal); var count = 0;
                void Walk(FormConditionGroup? group, int depth)
                {
                    if (group is null || depth > 8 || ++count > 200 || group.Conditions is null || group.Groups is null)
                        throw new FormatException("Invalid or excessive condition group.");
                    foreach (var condition in group.Conditions)
                    { if (condition is null || ++count > 200) throw new FormatException("Invalid condition."); names.Add(condition.Field); }
                    foreach (var child in group.Groups) Walk(child, depth + 1);
                }
                if (!rule.Always) Walk(rule.When, 0);
                var sources = names.Select(name => config.Sources.SingleOrDefault(s => s?.Key == name)
                    ?? throw new FormatException("Unknown source: " + name)).ToList();
                foreach (var source in sources)
                {
                    if (string.IsNullOrWhiteSpace(source.NodeKey) || string.IsNullOrWhiteSpace(source.Field) || !SupportedType(source.Type))
                        throw new FormatException("Invalid visited source definition: " + source.Label);
                    if (source.NodeKey != "$input" && !values.ContainsKey(source.NodeKey + ".$outcome"))
                        throw new FormatException("The source has not completed in this branch: " + source.Label);
                    if (!values.TryGetValue(source.Key, out var value) || value is JsonElement { ValueKind: JsonValueKind.Undefined })
                        throw new FormatException("Missing source value: " + source.Label);
                    var json = JsonSerializer.SerializeToElement(value);
                    // FormGateway's SQL adapter stores multiple-choice values as JSON text.
                    if (source.Type == "multiple_choice" && json.ValueKind == JsonValueKind.String)
                    {
                        try { json = JsonSerializer.Deserialize<JsonElement>(json.GetString()!); }
                        catch (JsonException) { throw new FormatException("Malformed multiple-choice source: " + source.Label); }
                    }
                    if (json.ValueKind == JsonValueKind.Null) continue;
                    var valid = source.Type switch
                    {
                        "numeric" => json.ValueKind == JsonValueKind.Number && json.TryGetDecimal(out _),
                        "yes_no" => json.ValueKind is JsonValueKind.True or JsonValueKind.False,
                        "multiple_choice" => json.ValueKind == JsonValueKind.Array && json.EnumerateArray().All(v => v.ValueKind == JsonValueKind.String),
                        _ => json.ValueKind == JsonValueKind.String
                    };
                    if (!valid) throw new FormatException("Invalid source type: " + source.Label);
                }
                var matched = rule.Always || WorkflowFormRouting.Decide(new ActivityFormStep
                { Rules = [new() { Id = rule.TransitionKey, When = rule.When }] }, sources.Select(s => s.AsField()).ToList(), values).RuleId is not null;
                trace.Add(new(rule.TransitionKey, matched ? "Matched" : "Checked"));
                if (matched) return Finish(rule.TransitionKey);
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException or OverflowException)
            { trace.Add(new(rule?.TransitionKey ?? "", "Checked", ex.Message)); return Finish(null, ex.Message); }
        }
        return Finish(null);
    }
}
