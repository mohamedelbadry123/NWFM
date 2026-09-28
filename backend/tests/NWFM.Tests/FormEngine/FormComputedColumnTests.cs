namespace NWFM.Tests.Modules.FormEngine;

using System.Text.Json;
using FluentAssertions;
using global::FormEngine.Application.Common.Schema;
using global::FormEngine.Application.Common.Schema.Expressions;
using global::FormEngine.Domain.Constants;

/// <summary>
/// Computed columns: the expression language their values are written in, how a column picks its
/// value from its rules, what the designer is refused, and how the schema carries them.
/// </summary>
public sealed class FormComputedColumnTests
{
    private static IReadOnlyDictionary<string, object?> Answers(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);

    private static FormExpressionValue Eval(string expression, IReadOnlyDictionary<string, object?>? answers = null) =>
        FormExpression.Parse(expression).Evaluate(answers ?? Answers());

    // --- The expression language ------------------------------------------------------------------

    [Theory]
    [InlineData("1 + 2 * 3", 7)]
    [InlineData("(1 + 2) * 3", 9)]
    [InlineData("-2 + 5", 3)]
    [InlineData("10 / 4", 2.5)]
    [InlineData("round(10 / 3, 2)", 3.33)]
    [InlineData("round(2.5)", 3)]
    [InlineData("abs(-4)", 4)]
    [InlineData("sum(1, 2, 3)", 6)]
    [InlineData("min(4, 2, 9)", 2)]
    [InlineData("max(4, 2, 9)", 9)]
    public void Arithmetic_FollowsPrecedence(string expression, double expected) =>
        Eval(expression).Number.Should().Be((decimal)expected);

    [Fact]
    public void Fields_AreReadFromTheAnswers_WhetherJsonOrClr()
    {
        using var doc = JsonDocument.Parse("""{"a": 4, "b": "6"}""");
        var answers = Answers(("a", doc.RootElement.GetProperty("a")), ("b", doc.RootElement.GetProperty("b")), ("c", 2.5));

        Eval("a + b + c", answers).Number.Should().Be(12.5m);
    }

    [Fact]
    public void ABlankOrTextOperand_MakesArithmeticBlank_ButSumSkipsIt()
    {
        var answers = Answers(("a", 5), ("b", null), ("c", "abc"));

        Eval("a + b", answers).IsBlank.Should().BeTrue();
        Eval("a * c", answers).IsBlank.Should().BeTrue();
        Eval("a + missing", answers).IsBlank.Should().BeTrue();
        Eval("sum(a, b, c, missing)", answers).Number.Should().Be(5);
    }

    [Fact]
    public void DivisionByZero_IsBlank_NotAnError() =>
        Eval("1 / 0").IsBlank.Should().BeTrue();

    [Fact]
    public void Text_LiteralsConcatAndCoalesce()
    {
        var answers = Answers(("name", "Riyadh"), ("empty", ""));

        Eval("'High'").Text.Should().Be("High");
        Eval("'it''s'").Text.Should().Be("it's");
        Eval("concat(name, ' - ', 3)", answers).Text.Should().Be("Riyadh - 3");
        Eval("coalesce(empty, missing, 'n/a')", answers).Text.Should().Be("n/a");
    }

    [Fact]
    public void ANameFollowedByAParen_IsAFunction_OtherwiseAField()
    {
        var expression = FormExpression.Parse("sum(sum, 1)");

        expression.ReferencedFields.Should().Equal("sum");
        expression.Evaluate(Answers(("sum", 2))).Number.Should().Be(3);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("1 +", 3)]
    [InlineData("(1 + 2", 6)]
    [InlineData("1 $ 2", 2)]
    [InlineData("'open", 0)]
    [InlineData("nope(1)", 0)]
    [InlineData("abs(1, 2)", 0)]
    public void Parse_SaysWhereItWentWrong(string expression, int position) =>
        FluentActions.Invoking(() => FormExpression.Parse(expression))
            .Should().Throw<FormExpressionException>()
            .Which.Position.Should().Be(position);

    [Fact]
    public void Parse_RefusesAnExpressionTooLongOrTooDeep()
    {
        FluentActions.Invoking(() => FormExpression.Parse(new string('1', FormExpression.MaxLength + 1)))
            .Should().Throw<FormExpressionException>();

        var deep = new string('(', 60) + "1" + new string(')', 60);
        FluentActions.Invoking(() => FormExpression.Parse(deep)).Should().Throw<FormExpressionException>();
    }

    // --- Picking a value --------------------------------------------------------------------------

    private static FormRuleGroup When(string field, string op, string value, string match = FormRuleMatches.All) =>
        new(match, [new FormRuleCondition(field, op, value)]);

    private static FormSchema Schema(params FormComputedColumn[] columns) =>
        new(null, null, [new FormSchemaField("leak_size", "numeric", "Size", "الحجم"), new FormSchemaField("pipe", "text", "Pipe", "الأنبوب")], columns);

    private static FormComputedColumn Severity() =>
        new(
            "severity",
            "Severity",
            "الخطورة",
            FormComputedOutputTypes.Text,
            true,
            [
                new FormComputedRule(When("leak_size", FormRuleOperators.GreaterThan, "10"), "'High'"),
                new FormComputedRule(When("leak_size", FormRuleOperators.GreaterThan, "3"), "'Medium'"),
            ],
            "'Low'");

    [Theory]
    [InlineData(12, "High")]
    [InlineData(5, "Medium")]
    [InlineData(1, "Low")]
    public void Evaluate_TheFirstRuleThatHoldsWins_ElseTheDefault(int size, string expected) =>
        FormComputedColumnEvaluator.Evaluate(Schema(Severity()), Answers(("leak_size", size)))
            .Should().ContainSingle().Which.Text.Should().Be(expected);

    [Fact]
    public void Evaluate_ANumberColumn_CarriesTheNumberAndItsText()
    {
        var column = new FormComputedColumn("area", "Area", "المساحة", FormComputedOutputTypes.Number, false, [], "round(leak_size * 1.5, 1)");

        var result = FormComputedColumnEvaluator.Evaluate(Schema(column), Answers(("leak_size", 3))).Single();

        result.Number.Should().Be(4.5m);
        result.Text.Should().Be("4.5");
    }

    [Fact]
    public void Evaluate_ANumberColumnWorkingOutToText_IsBlank()
    {
        var column = new FormComputedColumn("n", "N", "ن", FormComputedOutputTypes.Number, false, [], "pipe");

        var result = FormComputedColumnEvaluator.Evaluate(Schema(column), Answers(("pipe", "steel"))).Single();

        result.Number.Should().BeNull();
        result.Text.Should().BeNull();
    }

    // --- What the designer is refused -------------------------------------------------------------

    [Fact]
    public void Validate_ASoundColumn_HasNoProblems() =>
        FormComputedColumnValidator.Validate(Schema(Severity())).Should().BeEmpty();

    [Fact]
    public void Validate_ReportsEachProblem()
    {
        var bad = new FormComputedColumn(
            "Bad Key",
            "Label",
            null,
            "date",
            false,
            [
                new FormComputedRule(null, "'x'"),
                new FormComputedRule(When("gone", FormRuleOperators.Equal, "1"), "missing_field + 1"),
                new FormComputedRule(When("pipe", FormRuleOperators.Equal, "1"), "1 +"),
            ],
            null);

        var problems = FormComputedColumnValidator.Validate(Schema(bad, Severity(), Severity()));

        problems.Should().Contain(p => p.Contains("key must start"));
        problems.Should().Contain(p => p.Contains("English and an Arabic label"));
        problems.Should().Contain(p => p.Contains("output type"));
        problems.Should().Contain(p => p.Contains("at least one condition"));
        problems.Should().Contain(p => p.Contains("'gone'"));
        problems.Should().Contain(p => p.Contains("'missing_field'"));
        problems.Should().Contain(p => p.Contains("ends too early"));
        problems.Should().Contain(p => p.Contains("more than once"));
    }

    // --- The schema -------------------------------------------------------------------------------

    [Fact]
    public void Parser_ReadsTheComputedColumns()
    {
        const string json = """
        {
          "name_en": "Leak", "name_ar": "تسرب",
          "elements": [{ "type": "numeric", "data_name": "leak_size", "label_en": "Size" }],
          "computed_columns": [{
            "key": "severity", "label_en": "Severity", "label_ar": "الخطورة",
            "output_type": "text", "show_in_task_grid": true,
            "rules": [{ "when": { "match": "all", "conditions": [{ "field": "leak_size", "operator": "greater_than", "value": "10" }] }, "then": "'High'" }],
            "default": "'Low'"
          }]
        }
        """;

        var column = FormSchemaParser.Parse(json).ComputedColumns.Should().ContainSingle().Subject;

        column.Key.Should().Be("severity");
        column.ShowInTaskGrid.Should().BeTrue();
        column.DefaultExpression.Should().Be("'Low'");
        column.Rules.Should().ContainSingle().Which.When!.Conditions.Should().ContainSingle().Which.Field.Should().Be("leak_size");
    }

    [Fact]
    public void Parser_ASchemaWithoutComputedColumns_HasNone() =>
        FormSchemaParser.Parse("""{ "elements": [] }""").ComputedColumns.Should().BeEmpty();
}
