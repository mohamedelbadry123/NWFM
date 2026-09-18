namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Infrastructure.Services;

public sealed class WorkflowTransitionEvaluatorTests
{
    private readonly WorkflowTransitionEvaluator _evaluator = new();
    private readonly DateTime _now = DateTime.UtcNow;
    private readonly Guid _versionId = Guid.NewGuid();
    private readonly Guid _fromId = Guid.NewGuid();
    private readonly Guid _toId = Guid.NewGuid();

    private WorkflowTransition MakeTransition(string key, int priority, bool isDefault = false, string? condition = null) =>
        WorkflowTransition.Create(_versionId, _fromId, _toId, key, priority, _now, isDefault, condition);

    private WorkflowVariable MakeVariable(string name, string? valueJson) =>
        WorkflowVariable.Create(Guid.NewGuid(), Guid.NewGuid(), name, VariableDataType.String, valueJson, _now);

    [Fact]
    public void Evaluate_MatchingCondition_ReturnsMatchedTransitionKey()
    {
        var transitions = new List<WorkflowTransition>
        {
            MakeTransition("t-approve", 1, condition: "Decision == Approve"),
            MakeTransition("t-default", 2, isDefault: true)
        };
        var variables = new List<WorkflowVariable> { MakeVariable("Decision", "\"Approve\"") };

        var result = _evaluator.Evaluate(transitions, variables);

        result.Should().Be("t-approve");
    }

    [Fact]
    public void Evaluate_NoMatchingCondition_FallsBackToIsDefaultTransition()
    {
        var transitions = new List<WorkflowTransition>
        {
            MakeTransition("t-approve", 1, condition: "Decision == Approve"),
            MakeTransition("t-default", 2, isDefault: true)
        };
        var variables = new List<WorkflowVariable> { MakeVariable("Decision", "\"Reject\"") };

        var result = _evaluator.Evaluate(transitions, variables);

        result.Should().Be("t-default");
    }

    [Fact]
    public void Evaluate_NoMatchingCondition_FallsBackToNullConditionTransition()
    {
        var transitions = new List<WorkflowTransition>
        {
            MakeTransition("t-approve", 1, condition: "Decision == Approve"),
            MakeTransition("t-fallback", 2, isDefault: false, condition: null)
        };
        var variables = new List<WorkflowVariable> { MakeVariable("Decision", "\"Other\"") };

        var result = _evaluator.Evaluate(transitions, variables);

        result.Should().Be("t-fallback");
    }

    [Fact]
    public void Evaluate_PriorityOrdering_SelectsLowestPriorityFirst()
    {
        var transitions = new List<WorkflowTransition>
        {
            MakeTransition("t-low", 10, condition: "Status == Active"),
            MakeTransition("t-high", 1, condition: "Status == Active"),
            MakeTransition("t-default", 99, isDefault: true)
        };
        var variables = new List<WorkflowVariable> { MakeVariable("Status", "\"Active\"") };

        var result = _evaluator.Evaluate(transitions, variables);

        result.Should().Be("t-high");
    }

    [Fact]
    public void Evaluate_EmptyTransitions_ReturnsNull()
    {
        var result = _evaluator.Evaluate(
            new List<WorkflowTransition>(),
            new List<WorkflowVariable>());

        result.Should().BeNull();
    }

    [Fact]
    public void Evaluate_JsonEncodedStringValue_MatchesUnquotedCondition()
    {
        var transitions = new List<WorkflowTransition>
        {
            MakeTransition("t-match", 1, condition: "Status == Approved")
        };
        var variables = new List<WorkflowVariable> { MakeVariable("Status", "\"Approved\"") };

        var result = _evaluator.Evaluate(transitions, variables);

        result.Should().Be("t-match");
    }

    [Fact]
    public void Evaluate_VariableNameLookup_IsCaseInsensitive()
    {
        var transitions = new List<WorkflowTransition>
        {
            MakeTransition("t-match", 1, condition: "DECISION == Yes")
        };
        var variables = new List<WorkflowVariable> { MakeVariable("decision", "\"Yes\"") };

        var result = _evaluator.Evaluate(transitions, variables);

        result.Should().Be("t-match");
    }

    [Fact]
    public void EvaluateAllMatching_ReturnsAllTrueConditions_WithoutDefault()
    {
        var transitions = new List<WorkflowTransition>
        {
            MakeTransition("t-a", 1, condition: "FlagA == Yes"),
            MakeTransition("t-b", 2, condition: "FlagB == Yes"),
            MakeTransition("t-default", 99, isDefault: true)
        };
        var variables = new List<WorkflowVariable>
        {
            MakeVariable("FlagA", "\"Yes\""),
            MakeVariable("FlagB", "\"Yes\"")
        };

        var result = _evaluator.EvaluateAllMatching(transitions, variables);

        result.Select(t => t.TransitionKey).Should().BeEquivalentTo("t-a", "t-b");
    }

    [Fact]
    public void EvaluateAllMatching_NoMatch_IncludesIsDefault()
    {
        var transitions = new List<WorkflowTransition>
        {
            MakeTransition("t-a", 1, condition: "FlagA == Yes"),
            MakeTransition("t-default", 2, isDefault: true)
        };
        var variables = new List<WorkflowVariable> { MakeVariable("FlagA", "\"No\"") };

        var result = _evaluator.EvaluateAllMatching(transitions, variables);

        result.Should().ContainSingle().Which.TransitionKey.Should().Be("t-default");
    }
}
