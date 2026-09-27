namespace FormEngine.Application.Common.Schema;

/// <summary>
/// A value worked out of a fill's answers and shown beside the task in the task grid — declared on
/// the form, under the schema's root <c>computed_columns</c>, so it is versioned with the form.
/// </summary>
/// <param name="Key">The column's name, unique within the form: <c>^[a-z][a-z0-9_]*$</c>.</param>
/// <param name="OutputType">One of <see cref="FormEngine.Domain.Constants.FormComputedOutputTypes"/>.</param>
/// <param name="ShowInTaskGrid">Whether the task grid shows it without being asked.</param>
/// <param name="Rules">Tried in order; the first whose conditions hold supplies the value.</param>
/// <param name="DefaultExpression">The value when no rule holds; blank when there is none.</param>
public sealed record FormComputedColumn(
    string Key,
    string? LabelEn,
    string? LabelAr,
    string OutputType,
    bool ShowInTaskGrid,
    IReadOnlyList<FormComputedRule> Rules,
    string? DefaultExpression);

/// <summary>
/// One rule of a computed column: when <see cref="When"/> holds, the column is <see cref="Then"/> —
/// an expression (see <see cref="Expressions.FormExpression"/>). <see cref="When"/> is null only in a
/// schema that failed validation; such a rule never applies.
/// </summary>
public sealed record FormComputedRule(FormRuleGroup? When, string Then);

/// <summary>A computed column's value for one fill: a number, a text, or nothing.</summary>
public sealed record FormComputedResult(string Key, string OutputType, decimal? Number, string? Text);
