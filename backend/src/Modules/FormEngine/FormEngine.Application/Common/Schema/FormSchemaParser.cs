using System.Globalization;
using System.Text.Json;
using FormEngine.Domain.Constants;

namespace FormEngine.Application.Common.Schema;

/// <summary>
/// One selectable option of a choice field. <c>Value</c> is what a submission stores; the labels are
/// what a human is shown. Cascading (<c>dependency_value</c>) only drives input, so it is not kept.
/// </summary>
public sealed record FormSchemaChoice(
    string Value,
    string? LabelEn,
    string? LabelAr);

/// <summary>
/// A <c>date</c> / <c>date_time</c> field's constraint on its answer: a rule relative to the system
/// clock (see <see cref="FormDateRules"/>), narrowed by optional fixed calendar bounds.
/// </summary>
/// <param name="MinDate">Inclusive lower bound. For a <c>date_time</c> it means 00:00 of that day.</param>
/// <param name="MaxDate">Inclusive upper bound. For a <c>date_time</c> it means 23:59 of that day.</param>
public sealed record FormDateConstraint(string Rule, DateOnly? MinDate, DateOnly? MaxDate)
{
    /// <summary>True when the constraint would actually refuse something.</summary>
    public bool Constrains => FormDateRules.Constrains(Rule) || MinDate.HasValue || MaxDate.HasValue;
}

/// <summary>
/// The rules a field places on its own answer, beyond the date constraint.
///
/// Only the ones the builder actually writes for a given type are meaningful: <c>min_length</c> /
/// <c>max_length</c> / <c>pattern</c> reach the JSON for <c>text</c> and <c>memo</c>, and <c>format</c> /
/// <c>min</c> / <c>max</c> for <c>numeric</c> alone — see `serialize()` in the Angular store. The
/// parser reads them wherever they appear, so it is the validator that gates each check on the
/// field's type; a <c>barcode</c> must not inherit a rule it never declared.
/// </summary>
/// <param name="Hidden">
/// Statically hidden. Such a field is never shown, never answered, and so is never required.
/// </param>
/// <param name="Disabled">
/// Read-only. Angular takes a disabled control out of the form group, which suppresses every
/// validator on it including <c>required</c> — so the fill form lets a blank one through and the
/// server has to as well.
/// </param>
/// <param name="Pattern">The author's regex, from <c>pattern.regex</c>.</param>
/// <param name="Format"><c>decimal</c> or <c>integer</c>; see <see cref="FormNumericFormats"/>.</param>
/// <param name="VisibleConditions">
/// Every group that has to hold for the field to be on screen — its own, plus one for each
/// enclosing section, outermost first. A field that is not shown is not required. Empty means the
/// field is always visible.
/// </param>
/// <param name="RequiredConditions">
/// When it has conditions, it <em>replaces</em> <see cref="Required"/> rather than narrowing it —
/// the builder writes `expressions['props.required']`, which overrides the static flag either way.
/// </param>
public sealed record FormFieldRules(
    bool Required,
    bool Hidden,
    bool Disabled,
    int? MinLength,
    int? MaxLength,
    string? Pattern,
    string? Format,
    double? Min,
    double? Max,
    IReadOnlyList<FormRuleGroup> VisibleConditions,
    FormRuleGroup? RequiredConditions)
{
    /// <summary>What a field carrying no rules at all reports.</summary>
    public static readonly FormFieldRules None =
        new(false, false, false, null, null, null, null, null, null, [], null);
}

/// <summary>A single non-container field flattened out of the form-builder schema.</summary>
/// <param name="AllowedExtensions">
/// Lower-case, dot-less extensions a file-backed field accepts. Empty means the field places no
/// restriction of its own — which is also what every non-file field reports.
/// </param>
/// <param name="AllowOther">
/// Whether a choice field offers "Other". Unlike <c>dependency_value</c>, this is not input-only:
/// it decides whether the field gets a companion free-text column — see <see cref="FormChoiceOther"/>.
/// </param>
/// <param name="DateConstraint">
/// The date rule a <c>date</c> / <c>date_time</c> field places on its answer, or <c>null</c> for
/// every other field and for one that sets no rule.
/// </param>
/// <param name="Rules">
/// Everything else the field requires of its answer. Never null once parsed — a field declaring
/// nothing reports <see cref="FormFieldRules.None"/>.
/// </param>
public sealed record FormSchemaField(
    string DataName,
    string FieldType,
    string? LabelEn,
    string? LabelAr,
    IReadOnlyList<FormSchemaChoice> Choices,
    IReadOnlyList<string> AllowedExtensions,
    bool AllowOther,
    FormDateConstraint? DateConstraint = null,
    FormFieldRules? Rules = null)
{
    public FormSchemaField(string dataName, string fieldType, string? labelEn, string? labelAr)
        : this(dataName, fieldType, labelEn, labelAr, [], [], false)
    {
    }

    public FormSchemaField(
        string dataName,
        string fieldType,
        string? labelEn,
        string? labelAr,
        IReadOnlyList<FormSchemaChoice> choices)
        : this(dataName, fieldType, labelEn, labelAr, choices, [], false)
    {
    }

    public FormSchemaField(
        string dataName,
        string fieldType,
        string? labelEn,
        string? labelAr,
        IReadOnlyList<FormSchemaChoice> choices,
        IReadOnlyList<string> allowedExtensions)
        : this(dataName, fieldType, labelEn, labelAr, choices, allowedExtensions, false)
    {
    }
}

/// <summary>The flattened form-builder schema (name + all leaf fields; sections are unwrapped).</summary>
public sealed record FormSchema(string? NameEn, string? NameAr, IReadOnlyList<FormSchemaField> Fields);

/// <summary>
/// Parses the form-builder "Fulcrum-style" schema JSON (<c>name_en</c>/<c>name_ar</c>/<c>elements</c>)
/// and flattens it into leaf fields. Section containers are unwrapped (their children are promoted);
/// the section element itself carries no value column.
/// </summary>
public static class FormSchemaParser
{
    private const string NameEnProperty = "name_en";
    private const string NameArProperty = "name_ar";
    private const string ElementsProperty = "elements";
    private const string TypeProperty = "type";
    private const string DataNameProperty = "data_name";
    private const string LabelEnProperty = "label_en";
    private const string LabelArProperty = "label_ar";
    private const string ChoicesProperty = "choices";
    private const string ValueProperty = "value";
    private const string AllowedExtensionsProperty = "allowed_extensions";
    private const string AllowOtherProperty = "allow_other";
    private const string DateRuleProperty = "date_rule";
    private const string MinDateProperty = "min_date";
    private const string MaxDateProperty = "max_date";
    private const string RequiredProperty = "required";
    private const string HiddenProperty = "hidden";
    private const string DisabledProperty = "disabled";
    private const string MinLengthProperty = "min_length";
    private const string MaxLengthProperty = "max_length";
    private const string PatternProperty = "pattern";
    private const string RegexProperty = "regex";
    private const string FormatProperty = "format";
    private const string MinProperty = "min";
    private const string MaxProperty = "max";
    private const string VisibleConditionsProperty = "visible_conditions";
    private const string RequiredConditionsProperty = "required_conditions";
    private const string MatchProperty = "match";
    private const string ConditionsProperty = "conditions";
    private const string FieldProperty = "field";
    private const string OperatorProperty = "operator";

    /// <summary>The one shape a fixed date bound is written in by the builder.</summary>
    private const string DateOnlyFormat = "yyyy-MM-dd";

    /// <summary>Leading characters the builder tolerates around an extension (<c>". PDF"</c>).</summary>
    private static readonly char[] ExtensionTrimChars = ['.', ' ', '\t'];

    public static FormSchema Parse(string? schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            return new FormSchema(null, null, []);
        }

        using var doc = JsonDocument.Parse(schemaJson);
        var root = doc.RootElement;

        var nameEn = GetString(root, NameEnProperty);
        var nameAr = GetString(root, NameArProperty);

        var fields = new List<FormSchemaField>();
        if (root.TryGetProperty(ElementsProperty, out var elements) && elements.ValueKind == JsonValueKind.Array)
        {
            Flatten(elements, fields);
        }

        return new FormSchema(nameEn, nameAr, fields);
    }

    /// <summary>True when the JSON parses and contains at least one element.</summary>
    public static bool HasElements(string? schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(schemaJson);
            return doc.RootElement.TryGetProperty(ElementsProperty, out var elements)
                && elements.ValueKind == JsonValueKind.Array
                && elements.GetArrayLength() > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool IsValidJson(string? schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            return false;
        }

        try
        {
            using var _ = JsonDocument.Parse(schemaJson);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Promotes every leaf field out of its section containers.
    /// </summary>
    /// <param name="enclosing">
    /// The visibility a section imposes on everything inside it, accumulated as the walk descends.
    /// A section's <c>hidden</c> flag and <c>visible_conditions</c> govern its children in the
    /// renderer, but flattening throws the section itself away — so the constraint has to travel
    /// down onto each child, or a required field inside a section nobody was shown would be
    /// demanded of them.
    /// </param>
    private static void Flatten(
        JsonElement elements,
        List<FormSchemaField> sink,
        SectionVisibility enclosing = default)
    {
        foreach (var element in elements.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var type = GetString(element, TypeProperty) ?? string.Empty;

            if (string.Equals(type, FormElementTypes.Section, StringComparison.OrdinalIgnoreCase))
            {
                if (element.TryGetProperty(ElementsProperty, out var children) && children.ValueKind == JsonValueKind.Array)
                {
                    Flatten(children, sink, enclosing.And(
                        GetBoolean(element, HiddenProperty),
                        ReadRuleGroup(element, VisibleConditionsProperty)));
                }

                continue;
            }

            var dataName = GetString(element, DataNameProperty);
            if (string.IsNullOrWhiteSpace(dataName))
            {
                continue;
            }

            sink.Add(new FormSchemaField(
                dataName.Trim(),
                type.Trim(),
                GetString(element, LabelEnProperty),
                GetString(element, LabelArProperty),
                ReadChoices(element),
                ReadAllowedExtensions(element),
                GetBoolean(element, AllowOtherProperty),
                ReadDateConstraint(element),
                ReadRules(element, enclosing)));
        }
    }

    /// <summary>
    /// What the sections around a field say about whether it is on screen. Both parts accumulate as
    /// the walk descends: any one section hiding the field is enough, and every section's rule has
    /// to hold as well as the field's own.
    /// </summary>
    /// <remarks>
    /// The groups are kept as a list rather than merged into one. Merging is only faithful while
    /// every group matches on <c>all</c> — folding an <c>any</c> group's conditions into a flat
    /// <c>all</c> list would quietly turn "either of these" into "both of these".
    /// </remarks>
    private readonly record struct SectionVisibility(bool Hidden, IReadOnlyList<FormRuleGroup>? Conditions)
    {
        public SectionVisibility And(bool hidden, FormRuleGroup? conditions)
        {
            if (!FormRuleEngine.HasConditions(conditions))
            {
                return this with { Hidden = Hidden || hidden };
            }

            return new SectionVisibility(
                Hidden || hidden,
                [.. Conditions ?? [], conditions!]);
        }

        /// <summary>Every group that has to hold for the field to be on screen, outermost first.</summary>
        public IReadOnlyList<FormRuleGroup> Combine(FormRuleGroup? own) =>
            FormRuleEngine.HasConditions(own)
                ? [.. Conditions ?? [], own!]
                : Conditions ?? [];
    }

    /// <summary>
    /// Reads a choice field's options. Non-choice fields simply carry no <c>choices</c> array, so the
    /// result is empty and nothing downstream has to branch on the field type here.
    /// </summary>
    private static IReadOnlyList<FormSchemaChoice> ReadChoices(JsonElement element)
    {
        if (!element.TryGetProperty(ChoicesProperty, out var choices) || choices.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<FormSchemaChoice>();

        foreach (var choice in choices.EnumerateArray())
        {
            if (choice.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var value = GetString(choice, ValueProperty);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            result.Add(new FormSchemaChoice(
                value.Trim(),
                GetString(choice, LabelEnProperty),
                GetString(choice, LabelArProperty)));
        }

        return result;
    }

    /// <summary>
    /// Reads a file-backed field's accepted extensions. Read unconditionally for the same reason as
    /// <see cref="ReadChoices"/>: a field that carries no <c>allowed_extensions</c> array yields an
    /// empty list, so no caller has to branch on the field type to ask the question.
    /// </summary>
    private static IReadOnlyList<string> ReadAllowedExtensions(JsonElement element)
    {
        if (!element.TryGetProperty(AllowedExtensionsProperty, out var extensions)
            || extensions.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<string>();

        foreach (var extension in extensions.EnumerateArray())
        {
            if (extension.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            // Mirrors `normalizeExtension` in the builder: '.PDF' and ' pdf ' both mean 'pdf'.
            var normalized = extension.GetString()?.Trim().TrimStart(ExtensionTrimChars).ToLowerInvariant();
            if (!string.IsNullOrEmpty(normalized) && !result.Contains(normalized))
            {
                result.Add(normalized);
            }
        }

        return result;
    }

    /// <summary>
    /// Reads everything a field asks of its answer besides the date constraint, folding in the
    /// visibility imposed by the sections it sits in.
    /// </summary>
    private static FormFieldRules ReadRules(JsonElement element, SectionVisibility enclosing)
    {
        var own = ReadRuleGroup(element, VisibleConditionsProperty);

        return new FormFieldRules(
            Required: GetBoolean(element, RequiredProperty),
            Hidden: enclosing.Hidden || GetBoolean(element, HiddenProperty),
            Disabled: GetBoolean(element, DisabledProperty),
            MinLength: GetInt32(element, MinLengthProperty),
            MaxLength: GetInt32(element, MaxLengthProperty),
            Pattern: ReadPattern(element),
            Format: GetString(element, FormatProperty),
            Min: GetDouble(element, MinProperty),
            Max: GetDouble(element, MaxProperty),
            VisibleConditions: enclosing.Combine(own),
            RequiredConditions: ReadRuleGroup(element, RequiredConditionsProperty));
    }

    /// <summary>
    /// The author's regex out of <c>pattern.regex</c>. The companion <c>description</c> is the
    /// message the fill form shows and has no meaning here.
    /// </summary>
    private static string? ReadPattern(JsonElement element)
    {
        if (!element.TryGetProperty(PatternProperty, out var pattern) || pattern.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var regex = GetString(pattern, RegexProperty);
        return string.IsNullOrEmpty(regex) ? null : regex;
    }

    /// <summary>
    /// Reads a visibility or requirement rule group. A group with no usable conditions reads as
    /// <c>null</c> — "no rule", which is not the same as a rule that is never satisfied.
    /// </summary>
    private static FormRuleGroup? ReadRuleGroup(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var group) || group.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!group.TryGetProperty(ConditionsProperty, out var conditions) || conditions.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var result = new List<FormRuleCondition>();

        foreach (var condition in conditions.EnumerateArray())
        {
            if (condition.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            // Trimmed to match the builder, which trims a condition's `field` on import for the same
            // reason it trims `data_name`: the two have to name the same key.
            var field = GetString(condition, FieldProperty)?.Trim();
            if (string.IsNullOrEmpty(field))
            {
                continue;
            }

            result.Add(new FormRuleCondition(
                field,
                GetString(condition, OperatorProperty) ?? string.Empty,
                GetString(condition, ValueProperty) ?? string.Empty));
        }

        return result.Count > 0
            ? new FormRuleGroup(GetString(group, MatchProperty) ?? FormRuleMatches.All, result)
            : null;
    }

    /// <summary>
    /// Reads a date field's constraint. Read unconditionally for the same reason as
    /// <see cref="ReadChoices"/>: a field carrying none yields <c>null</c>, so no caller has to
    /// branch on the field type to ask the question.
    /// </summary>
    /// <remarks>
    /// An unrecognised rule name reads as <see cref="FormDateRules.None"/> and a bound in any
    /// shape other than <see cref="DateOnlyFormat"/> is dropped. A schema written by a newer
    /// client than this server therefore relaxes the rule rather than refusing every answer to it —
    /// which is the safe direction: the fill still goes through, and the client that wrote the
    /// rule is still enforcing it.
    /// </remarks>
    private static FormDateConstraint? ReadDateConstraint(JsonElement element)
    {
        var rule = GetString(element, DateRuleProperty);
        var normalizedRule = FormDateRules.IsDefined(rule) ? rule! : FormDateRules.None;

        var constraint = new FormDateConstraint(
            normalizedRule,
            GetDateOnly(element, MinDateProperty),
            GetDateOnly(element, MaxDateProperty));

        return constraint.Constrains ? constraint : null;
    }

    private static DateOnly? GetDateOnly(JsonElement element, string property)
    {
        var text = GetString(element, property);

        return DateOnly.TryParseExact(text, DateOnlyFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? value
            : null;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// Reads a boolean flag. Anything the builder did not write as a JSON boolean — absent, null, a
    /// string — reads as <c>false</c>, so an older schema simply opts out of the flag.
    /// </summary>
    private static bool GetBoolean(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>
    /// Reads a numeric bound. Absent, null, or written as anything but a JSON number reads as "no
    /// bound" — the builder writes these as numbers or as null, and a bound it did not state is not
    /// one this server should invent.
    /// </summary>
    private static double? GetDouble(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var parsed)
            ? parsed
            : null;

    private static int? GetInt32(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var parsed)
            ? parsed
            : null;
}
