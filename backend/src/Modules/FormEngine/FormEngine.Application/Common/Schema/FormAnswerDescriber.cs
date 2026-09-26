using System.Globalization;
using System.Text.Json;
using NWFM.Shared.Integration.Forms;

namespace FormEngine.Application.Common.Schema;

/// <summary>
/// Reads a stored fill back as a person reads it: the question's own label beside the answer, in
/// the order the form lays its fields out. A stored answer is shaped for the column it lives in —
/// an option's machine value, a boolean, a JSON array of file references — so each is rendered here
/// once, for the task details screen and the PDF report alike, rather than in each client.
/// </summary>
public static class FormAnswerDescriber
{
    private const string ListSeparator = ", ";
    private const string OtherSeparator = " — ";
    private const string FileNameProperty = "name";
    private const string DateFormat = "yyyy-MM-dd";
    private const string DateTimeFormat = "yyyy-MM-dd HH:mm";

    private const string YesEn = "Yes";
    private const string NoEn = "No";
    private const string YesAr = "نعم";
    private const string NoAr = "لا";

    /// <summary>
    /// Every answered field of <paramref name="schema"/>, labelled and rendered in both languages.
    /// A choice offering "Other" folds its typed text into the answer rather than listing the
    /// companion as a nameless row of its own.
    /// </summary>
    public static IReadOnlyList<FormAnswerView> Describe(
        FormSchema schema,
        IReadOnlyDictionary<string, object?> answers)
    {
        // Stored rows come back keyed by column name; a data name matches its column case-insensitively.
        var byName = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in answers)
        {
            byName.TryAdd(key, value is DBNull ? null : value);
        }

        var views = new List<FormAnswerView>(schema.Fields.Count);

        foreach (var field in schema.Fields)
        {
            if (!byName.TryGetValue(field.DataName, out var value) || FormAnswerValues.IsBlank(value))
            {
                continue;
            }

            var view = DescribeField(field, value, byName);
            if (view is not null)
            {
                views.Add(view);
            }
        }

        return views;
    }

    private static FormAnswerView? DescribeField(
        FormSchemaField field,
        object? value,
        IReadOnlyDictionary<string, object?> answers)
    {
        string english;
        string arabic;
        FormAnswerPoint? point = null;

        if (FormElementTypes.IsMedia(field.FieldType))
        {
            var names = FileNames(value);
            if (names.Count == 0)
            {
                return null;
            }

            english = arabic = string.Join(ListSeparator, names);
        }
        else if (FormAnswerDisplay.IsGeolocation(field.FieldType))
        {
            if (FormGeolocation.TryRead(value, out var read))
            {
                point = new FormAnswerPoint(read.Latitude, read.Longitude, read.Address);
                english = arabic = FormGeolocation.Describe(read);
            }
            else
            {
                english = arabic = FormAnswerValues.AsText(value);
            }
        }
        else if (string.Equals(field.FieldType, FormElementTypes.YesNo, StringComparison.OrdinalIgnoreCase)
            && TryReadFlag(value, out var flag))
        {
            english = flag ? YesEn : NoEn;
            arabic = flag ? YesAr : NoAr;
        }
        else if (string.Equals(field.FieldType, FormElementTypes.Date, StringComparison.OrdinalIgnoreCase)
            && TryFormatDate(value, DateFormat, out var date))
        {
            english = arabic = date;
        }
        else if (string.Equals(field.FieldType, FormElementTypes.DateTime, StringComparison.OrdinalIgnoreCase)
            && TryFormatDate(value, DateTimeFormat, out var dateTime))
        {
            english = arabic = dateTime;
        }
        else
        {
            var (displayEn, displayAr) = FormAnswerDisplay.Resolve(field, value);
            var plain = FormAnswerValues.AsText(value);
            english = displayEn ?? plain;
            arabic = displayAr ?? plain;

            if (FormChoiceOther.NeedsCompanion(field)
                && answers.TryGetValue(FormChoiceOther.KeyFor(field.DataName), out var other)
                && FormAnswerValues.AsText(other) is { Length: > 0 } otherText)
            {
                english = WithOther(english, otherText);
                arabic = WithOther(arabic, otherText);
            }
        }

        return new FormAnswerView(field.DataName, field.FieldType, field.LabelEn, field.LabelAr, english, arabic, point);
    }

    /// <summary>
    /// The typed text in place of the "Other" sentinel, or after the answer when the option carried
    /// a label of its own.
    /// </summary>
    private static string WithOther(string display, string otherText) =>
        display.Contains(FormChoiceOther.Sentinel, StringComparison.Ordinal)
            ? display.Replace(FormChoiceOther.Sentinel, otherText, StringComparison.Ordinal)
            : display + OtherSeparator + otherText;

    /// <summary>
    /// The names carried by a media answer's <c>{ fileId, path, name, … }</c> references. The column
    /// holds the array as JSON text; a posted answer is a <see cref="JsonElement"/>.
    /// </summary>
    private static IReadOnlyList<string> FileNames(object? value)
    {
        switch (value)
        {
            case JsonElement json:
                return FileNamesOf(json);

            case string text when text.TrimStart().StartsWith('['):
                try
                {
                    using var document = JsonDocument.Parse(text);
                    return FileNamesOf(document.RootElement);
                }
                catch (JsonException)
                {
                    return [];
                }

            default:
                return [];
        }
    }

    private static IReadOnlyList<string> FileNamesOf(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var names = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty(FileNameProperty, out var name)
                && name.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(name.GetString()))
            {
                names.Add(name.GetString()!.Trim());
            }
        }

        return names;
    }

    private static bool TryReadFlag(object? value, out bool flag)
    {
        switch (value)
        {
            case bool stored:
                flag = stored;
                return true;

            case JsonElement { ValueKind: JsonValueKind.True }:
                flag = true;
                return true;

            case JsonElement { ValueKind: JsonValueKind.False }:
                flag = false;
                return true;

            case string text when bool.TryParse(text, out var parsed):
                flag = parsed;
                return true;

            default:
                flag = false;
                return false;
        }
    }

    /// <summary>A date column reads back as a <see cref="DateTime"/>; anything else keeps its stored text.</summary>
    private static bool TryFormatDate(object? value, string format, out string text)
    {
        switch (value)
        {
            case DateTime dateTime:
                text = dateTime.ToString(format, CultureInfo.InvariantCulture);
                return true;

            case DateTimeOffset offset:
                text = offset.ToString(format, CultureInfo.InvariantCulture);
                return true;

            case DateOnly date:
                text = date.ToString(DateFormat, CultureInfo.InvariantCulture);
                return true;

            default:
                text = string.Empty;
                return false;
        }
    }
}
