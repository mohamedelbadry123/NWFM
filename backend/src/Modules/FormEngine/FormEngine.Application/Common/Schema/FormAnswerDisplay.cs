using System.Text.Json;

namespace FormEngine.Application.Common.Schema;

/// <summary>
/// Turns a stored choice answer into what a human reads. A submission keeps the option's machine
/// <c>value</c> (<c>"option_1"</c>, or a JSON array of them for multi-select); the option's
/// <c>label_en</c>/<c>label_ar</c> live only in the form schema, so they are resolved here and
/// snapshotted alongside the answer — a later edit to the form cannot then rewrite past fills.
/// </summary>
public static class FormAnswerDisplay
{
    /// <summary>Separator between the labels of a multi-select answer.</summary>
    private const string MultiValueSeparator = ", ";

    /// <summary>Guards against a runaway array bloating a stored summary.</summary>
    private const int MaxChoiceValues = 25;

    /// <summary>
    /// The English and Arabic rendering of <paramref name="value"/> for a choice, geolocation or
    /// calendar-with-hours field, or <c>(null, null)</c> for any other field type — those already read fine
    /// as stored. An option the schema no longer knows about falls back to the stored value itself,
    /// which is also what an <c>allow_other</c> free-text entry needs. A point reads the same in both
    /// languages, so its coordinates are given for each; a shift grid names its days, so it does not.
    /// </summary>
    public static (string? DisplayEn, string? DisplayAr) Resolve(FormSchemaField? field, object? value)
    {
        if (field is null)
        {
            return (null, null);
        }

        if (IsGeolocation(field.FieldType))
        {
            return FormGeolocation.TryFormat(value, out var point) ? (point, point) : (null, null);
        }

        if (IsCalendarWithHours(field.FieldType))
        {
            return FormCalendarWithHours.TryFormat(value, out var shiftsEn, out var shiftsAr)
                ? (shiftsEn, shiftsAr)
                : (null, null);
        }

        if (!IsChoice(field.FieldType))
        {
            return (null, null);
        }

        var tokens = Tokenize(value);
        if (tokens.Count == 0)
        {
            return (null, null);
        }

        var byValue = new Dictionary<string, FormSchemaChoice>(StringComparer.OrdinalIgnoreCase);
        foreach (var choice in field.Choices)
        {
            byValue[choice.Value] = choice;
        }

        var english = new List<string>(tokens.Count);
        var arabic = new List<string>(tokens.Count);

        foreach (var token in tokens)
        {
            if (byValue.TryGetValue(token, out var choice))
            {
                english.Add(Coalesce(choice.LabelEn, choice.LabelAr, token));
                arabic.Add(Coalesce(choice.LabelAr, choice.LabelEn, token));
                continue;
            }

            english.Add(token);
            arabic.Add(token);
        }

        return (string.Join(MultiValueSeparator, english), string.Join(MultiValueSeparator, arabic));
    }

    public static bool IsChoice(string? fieldType) =>
        string.Equals(fieldType, FormElementTypes.SingleChoice, StringComparison.OrdinalIgnoreCase)
        || string.Equals(fieldType, FormElementTypes.MultipleChoice, StringComparison.OrdinalIgnoreCase);

    public static bool IsGeolocation(string? fieldType) =>
        string.Equals(fieldType, FormElementTypes.Geolocation, StringComparison.OrdinalIgnoreCase);

    public static bool IsCalendarWithHours(string? fieldType) =>
        string.Equals(fieldType, FormElementTypes.CalendarWithHours, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Flattens an answer into the option values it holds. Answers arrive as <see cref="JsonElement"/>
    /// from the API, but a value read back out of the submissions table is the raw JSON text, so both
    /// shapes are handled.
    /// </summary>
    private static IReadOnlyList<string> Tokenize(object? value)
    {
        switch (value)
        {
            case null:
                return [];

            case JsonElement json:
                return FromJson(json);

            case string text when text.TrimStart().StartsWith('['):
                try
                {
                    using var document = JsonDocument.Parse(text);
                    return FromJson(document.RootElement);
                }
                catch (JsonException)
                {
                    // Not an array after all — treat it as the single value it is.
                    return [text];
                }

            case string text:
                return string.IsNullOrWhiteSpace(text) ? [] : [text];

            default:
                return [value.ToString() ?? string.Empty];
        }
    }

    private static IReadOnlyList<string> FromJson(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                var items = new List<string>();
                foreach (var item in element.EnumerateArray())
                {
                    if (items.Count == MaxChoiceValues)
                    {
                        break;
                    }

                    var token = ScalarText(item);
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        items.Add(token);
                    }
                }

                return items;

            default:
                var single = ScalarText(element);
                return string.IsNullOrWhiteSpace(single) ? [] : [single];
        }
    }

    private static string? ScalarText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.GetRawText(),
        _ => null,
    };

    private static string Coalesce(string? preferred, string? fallback, string last)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred.Trim();
        }

        return !string.IsNullOrWhiteSpace(fallback) ? fallback.Trim() : last;
    }
}
