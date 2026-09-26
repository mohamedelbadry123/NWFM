namespace NWFM.Tests.Modules.FormEngine;

using FluentAssertions;
using global::FormEngine.Application.Common.Schema;
using global::FormEngine.Domain.Constants;

public sealed class FormRuleEngineTests
{
    private static FormRuleGroup Group(string match, params FormRuleCondition[] conditions) =>
        new(match, conditions);

    private static Dictionary<string, object?> Answers(params (string Key, object? Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);

    [Theory]
    [InlineData(FormRuleOperators.Equal, "steel", true)]
    [InlineData(FormRuleOperators.Equal, "pvc", false)]
    [InlineData(FormRuleOperators.NotEqual, "pvc", true)]
    [InlineData(FormRuleOperators.Contains, "tee", true)]
    [InlineData(FormRuleOperators.StartsWith, "ste", true)]
    [InlineData(FormRuleOperators.IsEmpty, "", false)]
    [InlineData(FormRuleOperators.IsNotEmpty, "", true)]
    public void Evaluate_AppliesTextOperators(string op, string value, bool expected)
    {
        var group = Group(FormRuleMatches.All, new FormRuleCondition("material", op, value));

        FormRuleEngine.Evaluate(group, Answers(("material", "steel"))).Should().Be(expected);
    }

    [Theory]
    [InlineData(FormRuleOperators.GreaterThan, "3", true)]
    [InlineData(FormRuleOperators.GreaterThan, "7", false)]
    [InlineData(FormRuleOperators.LessThan, "7", true)]
    public void Evaluate_ComparesNumbersTheWayTheBrowserDoes(string op, string value, bool expected)
    {
        var group = Group(FormRuleMatches.All, new FormRuleCondition("depth", op, value));

        FormRuleEngine.Evaluate(group, Answers(("depth", 5))).Should().Be(expected);
    }

    [Fact]
    public void Evaluate_AnUnansweredFieldIsNotLessThanAnything()
    {
        var group = Group(FormRuleMatches.All, new FormRuleCondition("depth", FormRuleOperators.LessThan, "5"));

        FormRuleEngine.Evaluate(group, Answers()).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_EqualAsksWhetherAMultiChoiceAnswerHoldsTheValue()
    {
        var group = Group(FormRuleMatches.All, new FormRuleCondition("impacts", FormRuleOperators.Equal, "soil"));

        FormRuleEngine.Evaluate(group, Answers(("impacts", new[] { "water", "soil" }))).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_AnyMatchesWhenOneConditionHolds()
    {
        var group = Group(
            FormRuleMatches.Any,
            new FormRuleCondition("a", FormRuleOperators.Equal, "1"),
            new FormRuleCondition("b", FormRuleOperators.Equal, "2"));

        FormRuleEngine.Evaluate(group, Answers(("a", "nope"), ("b", "2"))).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_AllRequiresEveryCondition()
    {
        var group = Group(
            FormRuleMatches.All,
            new FormRuleCondition("a", FormRuleOperators.Equal, "1"),
            new FormRuleCondition("b", FormRuleOperators.Equal, "2"));

        FormRuleEngine.Evaluate(group, Answers(("a", "1"), ("b", "nope"))).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_AnUnknownOperatorIsSatisfied_AsItIsInTheBuilder()
    {
        var group = Group(FormRuleMatches.All, new FormRuleCondition("a", "sounds_like", "1"));

        FormRuleEngine.Evaluate(group, Answers(("a", "zzz"))).Should().BeTrue();
    }

    [Fact]
    public void HasConditions_TellsNoRuleApartFromAnUnsatisfiedOne()
    {
        FormRuleEngine.HasConditions(null).Should().BeFalse();
        FormRuleEngine.HasConditions(Group(FormRuleMatches.All)).Should().BeFalse();

        // A condition naming no field is a blank row in the builder, not a rule.
        FormRuleEngine.HasConditions(Group(FormRuleMatches.All, new FormRuleCondition("", FormRuleOperators.Equal, "1")))
            .Should().BeFalse();

        FormRuleEngine.HasConditions(Group(FormRuleMatches.All, new FormRuleCondition("a", FormRuleOperators.Equal, "1")))
            .Should().BeTrue();
    }

    [Fact]
    public void EvaluateAll_RequiresEveryGroup_SoNestedSectionsBothHaveToHold()
    {
        var outer = Group(FormRuleMatches.All, new FormRuleCondition("a", FormRuleOperators.Equal, "1"));
        var inner = Group(FormRuleMatches.All, new FormRuleCondition("b", FormRuleOperators.Equal, "2"));

        FormRuleEngine.EvaluateAll([outer, inner], Answers(("a", "1"), ("b", "2"))).Should().BeTrue();
        FormRuleEngine.EvaluateAll([outer, inner], Answers(("a", "1"), ("b", "x"))).Should().BeFalse();
        FormRuleEngine.EvaluateAll([], Answers()).Should().BeTrue();
    }
}
