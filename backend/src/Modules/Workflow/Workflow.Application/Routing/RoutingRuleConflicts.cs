using System.Text.Json;
using Workflow.Application.Workspace;
namespace Workflow.Application.Routing;

/// <summary>Conservative authoring checks; execution continues to evaluate actual submitted values.</summary>
public static class RoutingRuleConflicts
{
    public static IReadOnlyList<string> Find(RoutingConfiguration config)
    {
        var conflicts = new List<string>();
        if (config.MatchMode != "Unique" || config.Routes is null) return conflicts;
        for (var i = 0; i < config.Routes.Count; i++)
            for (var j = i + 1; j < config.Routes.Count; j++)
                if (Overlaps(config.Routes[i], config.Routes[j])) conflicts.Add(config.Routes[j].TransitionKey);
        return conflicts.Distinct().ToArray();
    }
    private static bool Overlaps(RoutingRule a, RoutingRule b)
    {
        if (a.Always && b.Always) return true;
        if (a.Always || b.Always)
        {
            var other = a.Always ? b : a;
            if (other.When is null || other.When.Groups.Count != 0 || other.When.Conditions.Count != 1) return false;
            var condition = other.When.Conditions[0];
            return condition.Operator == "equal" || Range(condition) is { } interval && interval.Low <= interval.High;
        }
        if (a.When is null || b.When is null) return false;
        if (a.When.Groups.Count == 0 && b.When.Groups.Count == 0 && a.When.Conditions.Count == 1 && b.When.Conditions.Count == 1
            && a.When.Conditions[0].Operator == "equal" && JsonSerializer.Serialize(a.When) == JsonSerializer.Serialize(b.When)) return true;
        if (a.When.Groups.Count != 0 || b.When.Groups.Count != 0 || a.When.Conditions.Count != 1 || b.When.Conditions.Count != 1) return false;
        var x = a.When.Conditions[0]; var y = b.When.Conditions[0];
        if (x.Field != y.Field || Range(x) is not { } left || Range(y) is not { } right) return false;
        var lower = Math.Max(left.Low, right.Low); var upper = Math.Min(left.High, right.High);
        if (lower < upper) return true;
        return lower == upper && (lower != left.Low || left.LowClosed) && (lower != right.Low || right.LowClosed)
            && (upper != left.High || left.HighClosed) && (upper != right.High || right.HighClosed);
    }
    private static (double Low, bool LowClosed, double High, bool HighClosed)? Range(FormRoutingCondition condition)
    {
        if (condition.Value.ValueKind != JsonValueKind.Number || !condition.Value.TryGetDouble(out var value)) return null;
        return condition.Operator switch
        {
            "equal" => (value, true, value, true),
            "greater_than" => (value, false, double.PositiveInfinity, false),
            "greater_or_equal" => (value, true, double.PositiveInfinity, false),
            "less_than" => (double.NegativeInfinity, false, value, false),
            "less_or_equal" => (double.NegativeInfinity, false, value, true),
            "between" when condition.To.ValueKind == JsonValueKind.Number && condition.To.TryGetDouble(out var to) => (value, true, to, true),
            _ => null
        };
    }
}
