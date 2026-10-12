using System.Globalization;
using System.Text.Json;
using NWFM.Shared.Integration.Forms;
using Workflow.Application.Workspace;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

namespace Workflow.Application.Helpers;

/// <summary>Typed, bounded rules over a single canonical form submission. No expressions or scripts.</summary>
public static class WorkflowFormRouting
{
    public static (string? FormStepId, string? NodeKey) ResolveDestination(IReadOnlyList<ActivityFormStep> steps,
        ActivityFormStep step, FormRouteDestination destination, WorkflowVersion version, string sourceNodeKey)
    {
        if (destination.Kind == "Next")
        {
            var index = steps.ToList().FindIndex(s => s.Id == step.Id);
            if (index < 0) throw new FormatException("The active form is not configured.");
            return index + 1 < steps.Count ? (steps[index + 1].Id, null) : (null, null);
        }
        if (destination.Kind == "Form")
        {
            if (!steps.Any(s => s.Id == destination.Target)) throw new FormatException("Select a form in this activity.");
            return (destination.Target, null);
        }
        if (destination.Kind != "Activity") throw new FormatException("Select a supported routing destination.");
        var target = version.Activities.FirstOrDefault(a => a.NodeKey == destination.Target);
        if (target is null || target.ActivityType is not (ActivityType.UserTask or ActivityType.MainActivity or ActivityType.End))
            throw new FormatException("Select a business activity or End in this workflow.");
        // A conditional jump cannot escape a branch or enter a different branch.
        var source = version.Activities.First(a => a.NodeKey == sourceNodeKey);
        HashSet<string> Regions(Guid node)
        {
            var regions = new HashSet<string>();
            foreach (var fork in version.Activities.Where(a => a.ActivityType is ActivityType.ParallelGateway or ActivityType.InclusiveGateway))
                foreach (var branch in version.Transitions.Where(t => t.FromActivityDefinitionId == fork.Id))
                {
                    var seen = new HashSet<Guid>(); var pending = new Stack<Guid>(); pending.Push(branch.ToActivityDefinitionId);
                    while (pending.TryPop(out var current))
                    {
                        if (!seen.Add(current)) continue;
                        var activity = version.Activities.FirstOrDefault(a => a.Id == current);
                        if (activity is null || activity.ActivityType == ActivityType.JoinGateway) continue;
                        if (current == node) regions.Add(branch.TransitionKey);
                        foreach (var edge in version.Transitions.Where(t => t.FromActivityDefinitionId == current)) pending.Push(edge.ToActivityDefinitionId);
                    }
                }
            return regions;
        }
        if (!Regions(source.Id).SetEquals(Regions(target.Id))) throw new FormatException("Routing must stay inside the current execution branch and cannot bypass its join.");
        return (null, target.NodeKey);
    }

    public static IReadOnlyList<string> Operators(string type) => type switch
    {
        "numeric" or "date" or "date_time" or "time" => ["equal", "not_equal", "greater_than", "greater_or_equal", "less_than", "less_or_equal", "between", "is_empty", "is_not_empty"],
        "single_choice" => ["equal", "not_equal", "in", "not_in", "is_empty", "is_not_empty"],
        "multiple_choice" => ["contains_any", "contains_all", "contains_none", "is_empty", "is_not_empty"],
        "yes_no" => ["equal", "not_equal", "is_empty", "is_not_empty"],
        "text" or "memo" or "barcode" => ["equal", "not_equal", "contains", "starts_with", "ends_with", "is_empty", "is_not_empty"],
        _ => ["is_empty", "is_not_empty"]
    };

    public static IReadOnlyList<string> Validate(FormConditionGroup group, IReadOnlyList<FormFieldInfo> fields)
    {
        var errors = new List<string>(); var count = 0;
        void Walk(FormConditionGroup g, int depth)
        {
            if (depth > 8 || ++count > 200) { errors.Add("Condition groups exceed the supported size or depth."); return; }
            if (g.Match is not ("All" or "Any") || g.Conditions.Count + g.Groups.Count == 0) errors.Add("Each group needs conditions and All or Any matching.");
            foreach (var c in g.Conditions)
            {
                if (++count > 200) { errors.Add("Too many conditions."); return; }
                var field = fields.FirstOrDefault(f => f.DataName == c.Field);
                if (field is null) { errors.Add($"Field '{c.Field}' does not exist in the pinned form."); continue; }
                if (!Operators(field.FieldType).Contains(c.Operator)) { errors.Add($"Operator '{c.Operator}' is invalid for '{c.Field}'."); continue; }
                if (c.Operator is "is_empty" or "is_not_empty") continue;
                try
                {
                    var values = c.Operator is "in" or "not_in" or "contains_any" or "contains_all" or "contains_none"
                        ? c.Value.ValueKind == JsonValueKind.Array ? c.Value.EnumerateArray().ToList() : throw new FormatException("Select at least one option.")
                        : new List<JsonElement> { c.Value };
                    if (values.Count == 0) throw new FormatException("Select at least one option.");
                    foreach (var value in values)
                    {
                        if (Empty(value)) throw new FormatException("Enter a comparison value.");
                        if (field.FieldType is "single_choice" or "multiple_choice")
                        { if (!field.Choices.Any(choice => choice.Value == Text(value))) throw new FormatException("Select an option from the published form."); }
                        else Compare(field.FieldType, value, value);
                    }
                    if (c.Operator == "between" && Compare(field.FieldType, c.Value, c.To) > 0) throw new FormatException("The range start must not exceed its end.");
                }
                catch (Exception e) when (e is FormatException or InvalidOperationException or OverflowException) { errors.Add($"{c.Field}: {e.Message}"); }
            }
            foreach (var child in g.Groups) Walk(child, depth + 1);
        }
        Walk(group, 0); return errors;
    }

    public static (FormRouteDestination Destination, string? RuleId) Decide(ActivityFormStep step,
        IReadOnlyList<FormFieldInfo> fields, IReadOnlyDictionary<string, object?> answers)
    {
        if (step.Rules.Count > 100) throw new FormatException("Too many routing rules.");
        foreach (var rule in step.Rules)
        {
            var errors = Validate(rule.When, fields);
            if (errors.Count != 0) throw new FormatException(errors[0]);
        }
        // Validate submitted types before short-circuiting, so malformed values never turn into fallback.
        foreach (var field in fields.Where(f => f.FieldType is "numeric" or "date" or "date_time" or "time" or "yes_no"))
            if (answers.TryGetValue(field.DataName, out var value))
            { var json = AnswerJson(field.FieldType, value); if (!Empty(json)) Compare(field.FieldType, json, json); }
        bool Matches(FormConditionGroup group)
        {
            var checks = group.Conditions.Select(c => Test(c, fields.First(f => f.DataName == c.Field), answers))
                .Concat(group.Groups.Select(Matches));
            return group.Match == "All" ? checks.All(v => v) : checks.Any(v => v);
        }
        var matched = step.Rules.FirstOrDefault(r => Matches(r.When));
        return matched is null ? (step.Otherwise, null) : (matched.Destination, matched.Id);
    }

    private static bool Test(FormRoutingCondition c, FormFieldInfo field, IReadOnlyDictionary<string, object?> answers)
    {
        var answer = answers.TryGetValue(c.Field, out var value) ? AnswerJson(field.FieldType, value) : JsonSerializer.SerializeToElement<object?>(null);
        if (c.Operator == "is_empty") return Empty(answer);
        if (c.Operator == "is_not_empty") return !Empty(answer);
        if (Empty(answer)) return false;
        if (c.Operator is "in" or "not_in")
        { var found = c.Value.EnumerateArray().Any(v => Text(v) == Text(answer)); return c.Operator == "in" ? found : !found; }
        if (c.Operator is "contains_any" or "contains_all" or "contains_none")
        {
            if (answer.ValueKind != JsonValueKind.Array) throw new FormatException($"{c.Field}: Expected multiple-choice answers.");
            var actual = answer.EnumerateArray().Select(Text).ToHashSet(StringComparer.Ordinal);
            var desired = c.Value.EnumerateArray().Select(Text);
            return c.Operator switch { "contains_any" => desired.Any(actual.Contains), "contains_all" => desired.All(actual.Contains), _ => !desired.Any(actual.Contains) };
        }
        if (c.Operator is "contains" or "starts_with" or "ends_with")
            return c.Operator switch { "contains" => Text(answer).Contains(Text(c.Value), StringComparison.OrdinalIgnoreCase), "starts_with" => Text(answer).StartsWith(Text(c.Value), StringComparison.OrdinalIgnoreCase), _ => Text(answer).EndsWith(Text(c.Value), StringComparison.OrdinalIgnoreCase) };
        var comparison = Compare(field.FieldType, answer, c.Value);
        return c.Operator switch
        {
            "equal" => comparison == 0,
            "not_equal" => comparison != 0,
            "greater_than" => comparison > 0,
            "greater_or_equal" => comparison >= 0,
            "less_than" => comparison < 0,
            "less_or_equal" => comparison <= 0,
            "between" => comparison >= 0 && Compare(field.FieldType, answer, c.To) <= 0,
            _ => throw new FormatException("Unsupported comparison.")
        };
    }

    private static int Compare(string type, JsonElement left, JsonElement right)
    {
        var a = Text(left); var b = Text(right);
        return type switch
        {
            "numeric" => decimal.Parse(a, NumberStyles.Float, CultureInfo.InvariantCulture).CompareTo(decimal.Parse(b, NumberStyles.Float, CultureInfo.InvariantCulture)),
            "date" => ReadDate(a).Date.CompareTo(ReadDate(b).Date),
            "date_time" => ReadDate(a).CompareTo(ReadDate(b)),
            "time" => TimeOnly.ParseExact(a, ["HH:mm", "HH:mm:ss", "HH:mm:ss.FFFFFFF"], CultureInfo.InvariantCulture, DateTimeStyles.None).CompareTo(TimeOnly.ParseExact(b, ["HH:mm", "HH:mm:ss", "HH:mm:ss.FFFFFFF"], CultureInfo.InvariantCulture, DateTimeStyles.None)),
            "yes_no" => bool.Parse(a).CompareTo(bool.Parse(b)),
            "single_choice" => string.Compare(a, b, StringComparison.Ordinal),
            _ => string.Compare(a, b, StringComparison.OrdinalIgnoreCase)
        };
    }
    private static DateTime ReadDate(string value) => DateTime.ParseExact(value,
        ["yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF", "yyyy-MM-ddTHH:mm:ssK", "yyyy-MM-ddTHH:mm:ss.FFFFFFFK"],
        CultureInfo.InvariantCulture, DateTimeStyles.None);

    private static JsonElement AnswerJson(string type, object? value)
    {
        if (value is DateTime date) value = date.ToString(type == "date" ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        if (value is TimeSpan time) value = time.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        var json = JsonSerializer.SerializeToElement(value);
        // SQL stores multiple-choice and structured answers as JSON text. Compare their contents.
        if (json.ValueKind == JsonValueKind.String && type is "multiple_choice" or "photo" or "video" or "audio" or "file" or "signature" or "geolocation" or "calendar_with_hours")
        {
            var text = json.GetString();
            if (!string.IsNullOrWhiteSpace(text) && (text.TrimStart().StartsWith('[') || text.TrimStart().StartsWith('{')))
            {
                try { return JsonSerializer.Deserialize<JsonElement>(text); }
                catch (JsonException) { throw new FormatException("The stored form answer contains malformed JSON."); }
            }
        }
        return json;
    }
    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.ToString();
    private static bool Empty(JsonElement value) => value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
        || value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString())
        || value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0
        || value.ValueKind == JsonValueKind.Object && !value.EnumerateObject().Any();
}
