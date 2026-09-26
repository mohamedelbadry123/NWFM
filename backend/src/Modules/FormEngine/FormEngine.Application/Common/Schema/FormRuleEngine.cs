using FormEngine.Domain.Constants;

namespace FormEngine.Application.Common.Schema;

/// <summary>
/// One condition of a visibility or requirement rule: a field's answer, a comparison, and the text
/// to compare it against. <c>Value</c> is always a string — the builder types it into a box.
/// </summary>
public sealed record FormRuleCondition(string Field, string Operator, string Value);

/// <summary>
/// A field's visibility or requirement rule. <see cref="Match"/> is <c>all</c> or <c>any</c>;
/// anything else behaves as <c>all</c>, matching the builder.
/// </summary>
public sealed record FormRuleGroup(string Match, IReadOnlyList<FormRuleCondition> Conditions);

/// <summary>
/// Evaluates a form field's <c>visible_conditions</c> / <c>required_conditions</c> against a
/// posted answer set.
///
/// A line-for-line port of `form-builder-rules.ts`, which is what the fill form runs. It exists
/// because <c>required</c> cannot be enforced without it: a field may be required only under a
/// condition, and one hidden by a rule must never be demanded from a user who was never shown it.
/// A divergence here does not fail safe — it rejects valid fills.
/// </summary>
/// <remarks>
/// The browser evaluates these against the live form model; the server has only the submitted
/// answers, which is the same thing for a completed fill and the closest available stand-in
/// otherwise.
/// </remarks>
public static class FormRuleEngine
{
    /// <summary>
    /// Whether the group says anything at all. Conditions naming no field are dropped first, so a
    /// group holding only blank rows counts as empty — exactly what <c>buildRulePredicate</c> does
    /// before returning null.
    /// </summary>
    /// <remarks>
    /// Callers must ask this before <see cref="Evaluate"/>: an empty group means "no rule", not
    /// "false". Reading it as false would hide every field with no visibility rule.
    /// </remarks>
    public static bool HasConditions(FormRuleGroup? group) => Active(group).Count > 0;

    /// <summary>
    /// Whether the answers satisfy the group. Vacuously true when there is nothing to evaluate —
    /// guard with <see cref="HasConditions"/> to tell "satisfied" from "no rule".
    /// </summary>
    public static bool Evaluate(FormRuleGroup? group, IReadOnlyDictionary<string, object?> answers)
    {
        var conditions = Active(group);

        if (conditions.Count == 0)
        {
            return true;
        }

        return string.Equals(group!.Match, FormRuleMatches.Any, StringComparison.OrdinalIgnoreCase)
            ? conditions.Any(condition => Matches(condition, answers))
            : conditions.All(condition => Matches(condition, answers));
    }

    /// <summary>
    /// Whether the answers satisfy every group. Used for visibility, where a field carries its own
    /// rule plus one for each section it sits in and all of them have to hold for it to be on
    /// screen. An empty list is vacuously true — the field is always visible.
    /// </summary>
    public static bool EvaluateAll(
        IReadOnlyList<FormRuleGroup> groups,
        IReadOnlyDictionary<string, object?> answers) =>
        groups.All(group => Evaluate(group, answers));

    private static IReadOnlyList<FormRuleCondition> Active(FormRuleGroup? group) =>
        group?.Conditions.Where(condition => !string.IsNullOrEmpty(condition.Field)).ToList() ?? [];

    private static bool Matches(FormRuleCondition condition, IReadOnlyDictionary<string, object?> answers)
    {
        var answer = FormAnswerValue.Read(answers, condition.Field);
        var expected = condition.Value;

        return condition.Operator switch
        {
            FormRuleOperators.Equal => Equals(answer, expected),
            FormRuleOperators.NotEqual => !Equals(answer, expected),
            FormRuleOperators.Contains => Contains(answer, expected),
            FormRuleOperators.StartsWith => FormAnswerValues
                .AsText(answer)
                .StartsWith(expected, StringComparison.OrdinalIgnoreCase),
            FormRuleOperators.GreaterThan => Compare(answer, expected, (left, right) => left > right),
            FormRuleOperators.LessThan => Compare(answer, expected, (left, right) => left < right),
            FormRuleOperators.IsEmpty => FormAnswerValues.IsBlank(answer),
            FormRuleOperators.IsNotEmpty => !FormAnswerValues.IsBlank(answer),
            // An operator this server does not know is satisfied, as it is in the builder.
            _ => true,
        };
    }

    /// <summary>
    /// A multi-choice answer is an array, and "equal" asks whether it holds the value — the builder
    /// offers one operator for "is this option chosen" whether the field takes one answer or many.
    /// Case-sensitive, because it compares stored option values rather than anything a human typed.
    /// </summary>
    private static bool Equals(FormAnswerValue answer, string expected)
    {
        if (FormAnswerValues.AsList(answer.Value) is { } items)
        {
            return items.Any(item => string.Equals(FormAnswerValues.AsText(item), expected, StringComparison.Ordinal));
        }

        return string.Equals(FormAnswerValues.AsText(answer), expected, StringComparison.Ordinal);
    }

    /// <summary>
    /// Membership for an array answer, substring for a scalar one. The asymmetry — the array branch
    /// is case-sensitive, the text branch is not — is the builder's, and is kept so a rule behaves
    /// the same on both sides.
    /// </summary>
    private static bool Contains(FormAnswerValue answer, string expected)
    {
        if (FormAnswerValues.AsList(answer.Value) is { } items)
        {
            return items.Any(item => string.Equals(FormAnswerValues.AsText(item), expected, StringComparison.Ordinal));
        }

        return FormAnswerValues
            .AsText(answer)
            .Contains(expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Numeric comparison through JavaScript's own coercion. Either side failing to become a number
    /// makes the condition false rather than throwing — an unanswered field is not "less than 5".
    /// </summary>
    private static bool Compare(FormAnswerValue answer, string expected, Func<double, double, bool> matches)
    {
        var left = FormAnswerValues.ToNumber(answer);
        var right = FormAnswerValues.ToNumber(expected);

        return !double.IsNaN(left) && !double.IsNaN(right) && matches(left, right);
    }
}
