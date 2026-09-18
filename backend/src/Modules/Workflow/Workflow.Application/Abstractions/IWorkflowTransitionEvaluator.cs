namespace Workflow.Application.Abstractions;

using Workflow.Domain.Entities;

/// <summary>
/// Evaluates outgoing transitions from gateway nodes against instance variables.
/// Conditions are simple "variableName == value" expressions.
/// </summary>
public interface IWorkflowTransitionEvaluator
{
    /// <summary>
    /// ExclusiveGateway: returns the TransitionKey of the first matching condition,
    /// or the default (IsDefault / null-condition) transition if none match.
    /// </summary>
    string? Evaluate(
        IReadOnlyList<WorkflowTransition> outgoingTransitions,
        IReadOnlyList<WorkflowVariable> variables);

    /// <summary>
    /// InclusiveGateway: returns ALL transitions whose condition is true.
    /// If none match, returns the default transition(s) (IsDefault or null-condition).
    /// </summary>
    IReadOnlyList<WorkflowTransition> EvaluateAllMatching(
        IReadOnlyList<WorkflowTransition> outgoingTransitions,
        IReadOnlyList<WorkflowVariable> variables);
}
