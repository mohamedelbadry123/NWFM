using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FormEngine.Domain.Constants;

namespace FormEngine.Application.Common.Schema;

/// <summary>One field's answer refused, and why. <see cref="DataName"/> names the offending field.</summary>
public sealed record FormAnswerError(string DataName, string Message);

/// <summary>
/// Checks submitted answers against the rules their fields declare in the form schema.
///
/// The client is the first line — the fill form refuses a bad answer before submit — but a client is
/// not a gatekeeper: the API takes posts from the web app, from the offline mobile app, and from
/// anything holding a token, so a rule that only the browser applies is a suggestion. This is where
/// it becomes a rule.
/// </summary>
/// <remarks>
/// Every rule here is a port of one the fill form already applies, and the two have to agree.
/// A server that is <em>stricter</em> than the form rejects fills the user was told were valid,
/// which is worse than the gap it closes — so the coercion helpers in
/// <see cref="FormAnswerValues"/> follow the browser's semantics rather than the CLR's, including
/// where those are surprising.
/// <para>
/// Media rules (<c>max_files</c>, <c>max_file_size_mb</c>, <c>allowed_extensions</c>) are not
/// checked here: the answer holds file references, and the bytes were already vetted by the upload
/// endpoint, which enforces the extension list against the same schema.
/// </para>
/// </remarks>
public static class FormAnswerValidator
{
    /// <summary>
    /// Longest a field's own regex may run before it is abandoned. An author-supplied pattern is
    /// harmless in the browser that typed it and a denial-of-service vector here, where it runs on
    /// the server against whatever a caller posts.
    /// </summary>
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// The answers that break their field's rules. Empty when everything checks out.
    /// </summary>
    /// <param name="now">
    /// The wall clock the today-relative date rules are measured against, already in the timezone
    /// the answers were recorded in. A fill carries calendar values, not instants, so comparing them
    /// to a UTC clock would move "today" for anyone east or west of Greenwich.
    /// </param>
    /// <param name="enforceFieldRules">
    /// Whether to apply <c>required</c> and the scalar rules. Date rules are enforced either way —
    /// they shipped before this switch existed and nothing depends on their being off.
    /// </param>
    public static IReadOnlyList<FormAnswerError> Validate(
        FormSchema schema,
        IReadOnlyDictionary<string, object?> answers,
        DateTime now,
        bool enforceFieldRules = true)
    {
        var errors = new List<FormAnswerError>();

        foreach (var field in schema.Fields)
        {
            var answer = FormAnswerValue.Read(answers, field.DataName);
            var rules = field.Rules ?? FormFieldRules.None;

            if (FormAnswerValues.IsBlank(answer))
            {
                // An empty field has no shape to be wrong, so `required` is the only question worth
                // asking of it — every other rule below describes a value that is there.
                if (enforceFieldRules && IsRequired(rules, answers))
                {
                    Add(errors, field, "is required.");
                }

                continue;
            }

            if (enforceFieldRules)
            {
                CheckShape(errors, field, rules, answer);
            }

            CheckDate(errors, field, answer, now);
        }

        return errors;
    }

    /// <summary>
    /// Whether the field must be answered, as the fill form would decide it.
    ///
    /// A field nobody was shown, or nobody could type into, cannot be demanded of them: a static
    /// <c>hidden</c>, a <c>disabled</c> (read-only) control, or a visibility rule that does not hold
    /// all settle the question before <c>required</c> is consulted. A requirement rule then
    /// <em>replaces</em> the static flag rather than narrowing it — the builder writes it as an
    /// expression over <c>props.required</c>, which overrides the flag in both directions.
    /// </summary>
    private static bool IsRequired(FormFieldRules rules, IReadOnlyDictionary<string, object?> answers)
    {
        // Angular takes a disabled control out of the form group, which suppresses `required` along
        // with every other validator on it. The fill form therefore submits a blank one happily, and
        // a server that refused it would reject a fill the user was told was complete.
        if (rules.Hidden || rules.Disabled)
        {
            return false;
        }

        if (!FormRuleEngine.EvaluateAll(rules.VisibleConditions, answers))
        {
            return false;
        }

        return FormRuleEngine.HasConditions(rules.RequiredConditions)
            ? FormRuleEngine.Evaluate(rules.RequiredConditions, answers)
            : rules.Required;
    }

    /// <summary>
    /// The rules that describe a value rather than its presence, each gated on the field type that
    /// declares it — the builder only writes lengths and a pattern for <c>text</c>, and a format and
    /// bounds for <c>numeric</c>, so applying them elsewhere would enforce a rule the field never
    /// made.
    /// </summary>
    private static void CheckShape(
        List<FormAnswerError> errors,
        FormSchemaField field,
        FormFieldRules rules,
        FormAnswerValue answer)
    {
        switch (field.FieldType)
        {
            case FormElementTypes.Text:
            case FormElementTypes.Memo:
                CheckText(errors, field, rules, answer);
                break;

            case FormElementTypes.Numeric:
                CheckNumeric(errors, field, rules, answer);
                break;

            case FormElementTypes.SingleChoice:
            case FormElementTypes.MultipleChoice:
                CheckChoice(errors, field, answer);
                break;
        }
    }

    private static void CheckText(
        List<FormAnswerError> errors,
        FormSchemaField field,
        FormFieldRules rules,
        FormAnswerValue answer)
    {
        var text = FormAnswerValues.AsText(answer);

        if (rules.MinLength is { } minLength && text.Length < minLength)
        {
            Add(errors, field, $"must be at least {minLength} characters.");
            return;
        }

        if (rules.MaxLength is { } maxLength && text.Length > maxLength)
        {
            Add(errors, field, $"must be at most {maxLength} characters.");
            return;
        }

        if (rules.Pattern is { } pattern && !MatchesPattern(text, pattern))
        {
            Add(errors, field, "does not match the expected format.");
        }
    }

    /// <summary>
    /// Runs the author's regex the way Angular's <c>Validators.pattern</c> does — anchored at both
    /// ends, so an unanchored pattern means "the whole value looks like this" on both sides rather
    /// than "the value contains this" on one of them.
    /// </summary>
    /// <remarks>
    /// A pattern that will not compile, or that runs past <see cref="RegexTimeout"/>, passes. The
    /// alternative is refusing every answer to a field whose author wrote a bad regex, which turns a
    /// design-time mistake into an outage for users who cannot fix it.
    /// </remarks>
    private static bool MatchesPattern(string value, string pattern)
    {
        try
        {
            return Regex.IsMatch(value, $"^(?:{pattern})$", RegexOptions.None, RegexTimeout);
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (RegexMatchTimeoutException)
        {
            return true;
        }
    }

    private static void CheckNumeric(
        List<FormAnswerError> errors,
        FormSchemaField field,
        FormFieldRules rules,
        FormAnswerValue answer)
    {
        var value = FormAnswerValues.ToNumber(answer);

        if (double.IsNaN(value))
        {
            Add(errors, field, "must be a number.");
            return;
        }

        if (rules.Min is { } min && value < min)
        {
            Add(errors, field, $"must be greater than or equal to {Format(min)}.");
            return;
        }

        if (rules.Max is { } max && value > max)
        {
            Add(errors, field, $"must be less than or equal to {Format(max)}.");
            return;
        }

        if (string.Equals(rules.Format, FormNumericFormats.Integer, StringComparison.OrdinalIgnoreCase)
            && value != Math.Floor(value))
        {
            Add(errors, field, "must be a whole number.");
        }
    }

    /// <summary>
    /// Every selected value has to be one the field offers. A multiple-choice answer is an array, so
    /// each element is checked; a field with "Other" enabled also accepts the sentinel the client
    /// stores when the respondent typed their own answer into the companion field.
    /// </summary>
    private static void CheckChoice(
        List<FormAnswerError> errors,
        FormSchemaField field,
        FormAnswerValue answer)
    {
        // A schema whose options never parsed would otherwise refuse every answer to the field.
        if (field.Choices.Count == 0)
        {
            return;
        }

        var selected = FormAnswerValues.AsList(answer.Value) is { } items
            ? items.Select(FormAnswerValues.AsText)
            : [FormAnswerValues.AsText(answer)];

        foreach (var value in selected)
        {
            if (field.AllowOther && string.Equals(value, FormChoiceOther.Sentinel, StringComparison.Ordinal))
            {
                continue;
            }

            if (!field.Choices.Any(choice => string.Equals(choice.Value, value, StringComparison.Ordinal)))
            {
                Add(errors, field, $"has an answer that is not one of its options ('{value}').");
                return;
            }
        }
    }

    private static void CheckDate(
        List<FormAnswerError> errors,
        FormSchemaField field,
        FormAnswerValue answer,
        DateTime now)
    {
        if (field.DateConstraint is not { } constraint)
        {
            return;
        }

        if (Parse(answer.Value) is not { } value)
        {
            // Unparseable text is left alone: the submission store rejects it on the way into
            // the column, with a message naming the type it could not become.
            return;
        }

        var error = Check(value, constraint, now, ResolveGranularity(field.FieldType));

        if (error is not null)
        {
            Add(errors, field, error);
        }
    }

    private static void Add(List<FormAnswerError> errors, FormSchemaField field, string message) =>
        errors.Add(new FormAnswerError(field.DataName, $"{Describe(field)}: {message}"));

    /// <summary>A bound as the author wrote it — <c>120</c>, not <c>120.0</c>.</summary>
    private static string Format(double value) =>
        value == Math.Floor(value) && Math.Abs(value) < 1e21
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("R", CultureInfo.InvariantCulture);

    private static string? Check(
        DateTime value,
        FormDateConstraint constraint,
        DateTime now,
        bool toTheMinute)
    {
        var answer = toTheMinute ? Truncate(value) : value.Date;
        var reference = toTheMinute ? Truncate(now) : now.Date;

        var broken = constraint.Rule switch
        {
            FormDateRules.After => answer <= reference,
            FormDateRules.OnOrAfter => answer < reference,
            FormDateRules.Before => answer >= reference,
            FormDateRules.OnOrBefore => answer > reference,
            _ => false,
        };

        if (broken)
        {
            return RuleMessage(constraint.Rule, toTheMinute);
        }

        // The fixed bounds are whole days at either granularity — a max_date means the end of that
        // day — so a date_time is compared on its calendar half alone.
        var day = DateOnly.FromDateTime(value);

        if (constraint.MinDate is { } min && day < min)
        {
            return $"must not be before {Format(min)}.";
        }

        if (constraint.MaxDate is { } max && day > max)
        {
            return $"must not be after {Format(max)}.";
        }

        return null;
    }

    private static string RuleMessage(string rule, bool toTheMinute) => rule switch
    {
        FormDateRules.After => toTheMinute
            ? "must be later than the current time."
            : "must be a date after today.",
        FormDateRules.OnOrAfter => toTheMinute
            ? "must be the current time or later."
            : "must be today or a later date.",
        FormDateRules.Before => toTheMinute
            ? "must be earlier than the current time."
            : "must be a date before today.",
        FormDateRules.OnOrBefore => toTheMinute
            ? "must be the current time or earlier."
            : "must be today or an earlier date.",
        _ => "does not satisfy its date rule.",
    };

    /// <summary>
    /// A <c>date_time</c> is measured to the minute; a <c>date</c> to the calendar day, so any time
    /// of today counts as today.
    /// </summary>
    private static bool ResolveGranularity(string fieldType) =>
        string.Equals(fieldType, FormElementTypes.DateTime, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads an answer the same permissive way <c>FormSubmissionStore</c> does on the way into the
    /// column — the two have to agree, or a value one accepts the other refuses.
    /// </summary>
    private static DateTime? Parse(object? answer) => answer switch
    {
        DateTime moment => moment,
        DateTimeOffset moment => moment.DateTime,
        JsonElement { ValueKind: JsonValueKind.String } json => Parse(json.GetString()),
        string text => Parse(text),
        _ => null,
    };

    private static DateTime? Parse(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    /// <summary>Seconds dropped: the clients answer to the minute, so comparing below that is noise.</summary>
    private static DateTime Truncate(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Kind);

    private static string Format(DateOnly value) => value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>The field as the author named it, falling back to the column name.</summary>
    private static string Describe(FormSchemaField field) =>
        !string.IsNullOrWhiteSpace(field.LabelEn) ? field.LabelEn! : field.DataName;
}
