namespace Workflow.Infrastructure.Services;


using Workflow.Application.Abstractions;
using Workflow.Domain.Entities;

/// <summary>
/// Evaluates outgoing transitions for ExclusiveGateway / InclusiveGateway.
/// Conditions use the shared typed comparison language.
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
            .ThenBy(t => t.TransitionKey, StringComparer.Ordinal)
            .Where(t => Workflow.Application.Helpers.WorkflowCondition.Evaluate(t.ConditionExpression!, varMap))
            .ToList();

        if (matched.Count > 0)
            return matched;

        var defaults = outgoingTransitions
            .Where(t => t.IsDefault || string.IsNullOrWhiteSpace(t.ConditionExpression))
            .OrderBy(t => t.Priority)
            .ThenBy(t => t.TransitionKey, StringComparer.Ordinal)
            .ToList();

        return defaults.Count > 0
            ? defaults
            : Array.Empty<WorkflowTransition>();
    }

}
