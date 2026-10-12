namespace NWFM.Tests.Modules.Workflow;

using System.Text.Json;
using FluentAssertions;
using global::Workflow.Application.Helpers;
using global::Workflow.Application.Workspace;
using NWFM.Shared.Integration.Forms;

public sealed class WorkflowFormRoutingTests
{
    private static readonly FormFieldInfo[] Fields =
    [
        new("amount", "numeric", "Amount", "", null, []),
        new("date", "date", "Date", "", null, []),
        new("moment", "date_time", "Moment", "", null, []),
        new("time", "time", "Time", "", null, []),
        new("urgent", "yes_no", "Urgent", "", null, []),
        new("text", "text", "Text", "", null, []),
        new("status", "single_choice", "Status", "", null, [new("A", "Approved", "", null, null), new("B", "Review", "", null, null)]),
        new("tags", "multiple_choice", "Tags", "", null, [new("A", "A", "", null, null), new("B", "B", "", null, null)]),
        new("photo", "photo", "Photo", "", null, [])
    ];
    private static FormRoutingCondition Condition(string field, string op, object? value = null, object? to = null)
        => new() { Field = field, Operator = op, Value = JsonSerializer.SerializeToElement(value), To = JsonSerializer.SerializeToElement(to) };
    private static ActivityFormStep Step(params FormRoutingCondition[] conditions) => new()
    {
        Id = "first",
        Otherwise = new() { Kind = "Activity", Target = "end" },
        Rules = [new() { Id = "rule", When = new() { Conditions = conditions.ToList() }, Destination = new() { Kind = "Form", Target = "second" } }]
    };

    [Theory]
    [InlineData("amount", "greater_than", 10, 11, true)]
    [InlineData("amount", "less_or_equal", 10, 10, true)]
    [InlineData("amount", "greater_than", 10, 10, false)]
    [InlineData("date", "equal", "2026-10-09", "2026-10-09", true)]
    [InlineData("date", "less_than", "2026-10-09", "2026-10-08", true)]
    [InlineData("moment", "greater_than", "2026-10-09 10:00", "2026-10-09 10:01", true)]
    [InlineData("time", "equal", "10:00", "10:00:00", true)]
    [InlineData("urgent", "equal", false, false, true)]
    [InlineData("text", "equal", "OK", "ok", true)]
    [InlineData("text", "contains", "OK", "token OK now", true)]
    [InlineData("text", "starts_with", "ok", "OK now", true)]
    [InlineData("text", "ends_with", "ok", "now OK", true)]
    [InlineData("status", "equal", "A", "A", true)]
    [InlineData("status", "not_equal", "A", null, false)]
    [InlineData("urgent", "is_not_empty", null, false, true)]
    [InlineData("photo", "is_empty", null, "[]", true)]
    public void Comparisons_UseTypedValuesAndExplicitMissingSemantics(string field, string op, object? expected, object? answer, bool matches)
    {
        var decision = WorkflowFormRouting.Decide(Step(Condition(field, op, expected)), Fields, new Dictionary<string, object?> { [field] = answer });
        decision.RuleId.Should().Be(matches ? "rule" : null);
    }

    [Fact]
    public void RangesAndSqlValues_AreComparedAtTheirPublishedGranularity()
    {
        var step = Step(Condition("date", "between", "2026-10-09", "2026-10-10"), Condition("time", "equal", "09:30"));
        WorkflowFormRouting.Decide(step, Fields, new Dictionary<string, object?> { ["date"] = new DateTime(2026, 10, 10), ["time"] = new TimeSpan(9, 30, 0) }).RuleId.Should().Be("rule");
        WorkflowFormRouting.Decide(Step(Condition("amount", "between", 10, 20)), Fields, new Dictionary<string, object?> { ["amount"] = 20m }).RuleId.Should().Be("rule");
    }

    [Fact]
    public void NestedGroups_FirstMatchingRuleWins()
    {
        var step = Step(Condition("text", "equal", "no"));
        step.Rules[0].When.Match = "Any";
        step.Rules[0].When.Groups.Add(new() { Conditions = [Condition("urgent", "equal", true), Condition("amount", "greater_than", 100)] });
        step.Rules.Add(new() { Id = "later", When = new() { Conditions = [Condition("urgent", "equal", true)] } });
        WorkflowFormRouting.Decide(step, Fields, new Dictionary<string, object?> { ["urgent"] = true, ["amount"] = 101 }).RuleId.Should().Be("rule");
        WorkflowFormRouting.Decide(step, Fields, new Dictionary<string, object?> { ["urgent"] = true, ["amount"] = 90 }).RuleId.Should().Be("later");
        WorkflowFormRouting.Decide(step, Fields, new Dictionary<string, object?> { ["urgent"] = false }).Destination.Target.Should().Be("end");
    }

    [Theory]
    [InlineData("contains_any", true)]
    [InlineData("contains_all", false)]
    [InlineData("contains_none", false)]
    public void MultipleChoices_ReadSqlJsonStrings(string op, bool matches)
        => WorkflowFormRouting.Decide(Step(Condition("tags", op, new[] { "A", "B" })), Fields,
            new Dictionary<string, object?> { ["tags"] = "[\"A\"]" }).RuleId.Should().Be(matches ? "rule" : null);

    [Fact]
    public void InvalidConditionsAndMalformedAnswers_DoNotBecomeFallback()
    {
        WorkflowFormRouting.Validate(new() { Conditions = [Condition("removed", "equal", "x")] }, Fields).Should().NotBeEmpty();
        WorkflowFormRouting.Validate(new() { Conditions = [Condition("amount", "between", 20, 10)] }, Fields).Should().NotBeEmpty();
        WorkflowFormRouting.Validate(new() { Conditions = [Condition("status", "equal", "removed")] }, Fields).Should().NotBeEmpty();
        WorkflowFormRouting.Validate(new() { Conditions = [Condition("photo", "equal", "x")] }, Fields).Should().NotBeEmpty();
        var run = () => WorkflowFormRouting.Decide(Step(Condition("amount", "equal", 10)), Fields, new Dictionary<string, object?> { ["amount"] = "invalid" });
        run.Should().Throw<FormatException>();
    }
}
