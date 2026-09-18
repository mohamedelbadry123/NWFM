namespace Workflow.Infrastructure.Services;

using System.Text.Json;
using Workflow.Application.Abstractions;
using Workflow.Domain.Entities;

/// <summary>
/// Evaluates outgoing transitions for ExclusiveGateway / InclusiveGateway.
/// Condition format: "variableName == value" (simple equality check).
/// The default transition (IsDefault=true or null ConditionExpression) is used when no condition matches.
/// </summary>
internal sealed class WorkflowTransitionEvaluator : IWorkflowTransitionEvaluator
{
    public string? Evaluate(
        IReadOnlyList<WorkflowTransition> outgoingTransitions,
        IReadOnlyList<WorkflowVariable> variables)
    {
        var matching = EvaluateAllMatching(outgoingTransitions, variables);
        return matching.FirstOrDefault()?.TransitionKey;
    }

    public IReadOnlyList<WorkflowTransition> EvaluateAllMatching(
        IReadOnlyList<WorkflowTransition> outgoingTransitions,
        IReadOnlyList<WorkflowVariable> variables)
    {
        if (outgoingTransitions.Count == 0)
            return Array.Empty<WorkflowTransition>();

        var varMap = variables.ToDictionary(v => v.VariableName, v => v.ValueJson,
            StringComparer.OrdinalIgnoreCase);

        var matched = outgoingTransitions
            .Where(t => !t.IsDefault && !string.IsNullOrWhiteSpace(t.ConditionExpression))
            .OrderBy(t => t.Priority)
            .Where(t => EvaluateCondition(t.ConditionExpression!, varMap))
            .ToList();

        if (matched.Count > 0)
            return matched;

        var defaults = outgoingTransitions
            .Where(t => t.IsDefault || string.IsNullOrWhiteSpace(t.ConditionExpression))
            .OrderBy(t => t.Priority)
            .ToList();

        return defaults.Count > 0
            ? defaults
            : Array.Empty<WorkflowTransition>();
    }

    private static bool EvaluateCondition(string expression, Dictionary<string, string?> vars)
    {
        // Format: "variableName == value"
        var parts = expression.Split("==", 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;

        var varName = parts[0];
        var expected = parts[1].Trim('"', '\'');

        if (!vars.TryGetValue(varName, out var rawValue)) return false;

        var actual = rawValue is null ? null
            : TryDeserializeString(rawValue) ?? rawValue.Trim('"', '\'');

        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryDeserializeString(string json)
    {
        try { return JsonSerializer.Deserialize<string>(json); }
        catch { return null; }
    }
}
