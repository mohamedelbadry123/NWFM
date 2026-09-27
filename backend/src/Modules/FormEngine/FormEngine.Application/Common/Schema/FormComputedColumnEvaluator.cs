using FormEngine.Application.Common.Schema.Expressions;
using FormEngine.Domain.Constants;

namespace FormEngine.Application.Common.Schema;

/// <summary>
/// Works out a form's computed columns for one fill. Each column's rules are tried in order and the
/// first whose conditions hold supplies the value; the default applies when none does. The value is
/// then read as the column's output type. Never throws — a column whose expression does not parse
/// (a schema that skipped validation) or cannot be worked out is simply blank.
/// </summary>
public static class FormComputedColumnEvaluator
{
    /// <summary>The longest text a computed column keeps — what the task grid stores and sorts on.</summary>
    public const int MaxTextLength = 400;

    public static IReadOnlyList<FormComputedResult> Evaluate(FormSchema schema, IReadOnlyDictionary<string, object?> answers)
    {
        if (schema.ComputedColumns.Count == 0)
        {
            return [];
        }

        var results = new List<FormComputedResult>(schema.ComputedColumns.Count);

        foreach (var column in schema.ComputedColumns)
        {
            var expression = column.Rules
                .FirstOrDefault(rule => FormRuleEngine.HasConditions(rule.When) && FormRuleEngine.Evaluate(rule.When, answers))
                ?.Then
                ?? column.DefaultExpression;

            var value = string.IsNullOrWhiteSpace(expression) || !FormExpression.TryParse(expression, out var parsed, out _)
                ? FormExpressionValue.Blank
                : parsed!.Evaluate(answers);

            results.Add(ToResult(column, value));
        }

        return results;
    }

    private static FormComputedResult ToResult(FormComputedColumn column, FormExpressionValue value)
    {
        if (column.OutputType == FormComputedOutputTypes.Number)
        {
            var number = value.AsNumber();
            return new FormComputedResult(column.Key, column.OutputType, number, number is null ? null : FormExpressionValue.OfNumber(number.Value).AsText());
        }

        var text = value.AsText();
        if (text is { Length: > MaxTextLength })
        {
            text = text[..MaxTextLength];
        }

        return new FormComputedResult(column.Key, FormComputedOutputTypes.Text, null, string.IsNullOrEmpty(text) ? null : text);
    }
}
