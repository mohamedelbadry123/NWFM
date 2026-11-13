using Workflow.Application.Helpers;
using Workflow.Application.Workspace;

namespace Workflow.Application.Routing;

/// <summary>The same bounded, typed decision function is used in live execution and simulation.</summary>
public static class RoutingEvaluator
{
    public static IReadOnlyList<string> Validate(RoutingConfiguration config)
    {
        var errors = new List<string>();
        if (config.Sources is null || config.Routes is null || config.Sources.Any(s => s is null) || config.Routes.Any(r => r is null || r.When is null)) return ["Routing rules and sources must be provided."];
        var groups = 0;
        bool CompleteGroup(FormConditionGroup? group, int depth = 0) => group is not null && depth <= 8 && ++groups <= 200
            && group.Conditions is not null && group.Groups is not null
            && group.Conditions.All(c => c is not null && !string.IsNullOrWhiteSpace(c.Field) && !string.IsNullOrWhiteSpace(c.Operator))
            && group.Groups.All(child => CompleteGroup(child, depth + 1));
        if (config.Routes.Any(r => !r.Always && !CompleteGroup(r.When))) return ["Complete each condition field and comparison within the supported group depth."];
        if (config.Sources.Any(s => string.IsNullOrWhiteSpace(s.Key) || string.IsNullOrWhiteSpace(s.NodeKey) || string.IsNullOrWhiteSpace(s.Field) || string.IsNullOrWhiteSpace(s.Type)))
            return ["Each source needs its element, field and type."];
        if (config.ContractVersion is not (1 or 2)) errors.Add("Unsupported routing contract version.");
        if (config.ContractVersion == 2 && config.MatchMode != "First") errors.Add("Routing contract 2 requires ordered First matching.");
        if (config.ContractVersion == 2 && config.Sources.Any(s => !RoutingSequentialEvaluator.SupportedType(s.Type))) errors.Add("Ordered routing requires supported typed source fields.");
        if (config.MatchMode is not ("Unique" or "First" or "All")) errors.Add("Select one matching route, priority, or all matches.");
        if (config.Sources.Count > 200 || config.Routes.Count > 100) return ["Routing configuration exceeds supported limits."];
        if (config.Sources.Select(s => s.Key).Distinct(StringComparer.Ordinal).Count() != config.Sources.Count)
            errors.Add("Each data source must have a unique key.");
        if (config.Routes.Select(s => s.TransitionKey).Distinct(StringComparer.Ordinal).Count() != config.Routes.Count)
            errors.Add("Each route must reference a different connection.");
        var fields = config.Sources.Select(s => s.AsField()).ToList();
        foreach (var route in config.Routes)
        {
            if (string.IsNullOrWhiteSpace(route.TransitionKey)) errors.Add("Choose the route destination.");
            if (!route.Always) errors.AddRange(WorkflowFormRouting.Validate(route.When, fields));
        }
        if (config.WaitFor is not ("All" or "Any")) errors.Add("Select ALL or ANY for the join.");
        if (config.RemainingBranches is not ("Continue" or "Cancel")) errors.Add("Select what happens to remaining branches.");
        if (config.DeadlineSeconds is <= 0 or > 31536000) errors.Add("The deadline must be between one second and one year.");
        return errors;
    }

    public static RoutingDecision Evaluate(RoutingConfiguration config, IReadOnlyDictionary<string, object?> values)
    {
        if (config.ContractVersion == 2) return RoutingSequentialEvaluator.Evaluate(config, values);
        var errors = Validate(config);
        if (errors.Count > 0) return new([], [], errors[0]);
        foreach (var source in config.Sources)
            if (source.NodeKey != "$input" && !values.ContainsKey(source.NodeKey + ".$outcome"))
                return new([], [], "The source has not completed in this branch: " + source.Label);
        var fields = config.Sources.Select(s => s.AsField()).ToList();
        var matches = new List<string>();
        try
        {
            // Evaluate every rule even in priority mode: malformed input must never become a fallback.
            foreach (var rule in config.Routes)
            {
                var step = new ActivityFormStep
                {
                    Rules = rule.Always ? [] : [new() { Id = rule.TransitionKey, When = rule.When }]
                };
                var decision = WorkflowFormRouting.Decide(step, fields, values);
                if (rule.Always || decision.RuleId is not null) matches.Add(rule.TransitionKey);
            }
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or OverflowException)
        { return new([], matches, ex.Message); }
        if (matches.Count == 0) return string.IsNullOrWhiteSpace(config.FallbackTransitionKey)
            ? new([], matches, "No condition matched. This route is stopped for attention.")
            : new([config.FallbackTransitionKey], matches, UsedFallback: true);
        if (config.MatchMode == "Unique" && matches.Count > 1)
            return new([], matches, "More than one route matched. Make the rules exclusive or explicitly select priority mode.");
        return new(config.MatchMode == "All" ? matches : [matches[0]], matches);
    }

    public static bool JoinReady(string mode, IReadOnlyCollection<string> states) => states.Count > 0 &&
        (mode == "Any" ? states.Any(s => s == "Met") : states.All(s => s == "Met"));

    public static bool JoinImpossible(string mode, IReadOnlyCollection<string> states) => states.Count > 0 &&
        (mode == "All" ? states.Any(s => s is "Failed" or "Cancelled" or "Unmet")
            : states.All(s => s is "Failed" or "Cancelled" or "Unmet"));
}
