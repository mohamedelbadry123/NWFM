using System.Text.Json;

namespace FormEngine.Application.Common.Schema;

/// <summary>One day's shift, as it was read out of a calendar-with-hours answer.</summary>
public readonly record struct FormCalendarDay(string Day, string From, string To);

/// <summary>
/// Reads and renders a <c>calendar_with_hours</c> answer — the weekly shift grid a paper form
/// calls <c>ساعات العمل</c>.
///
/// The answer is a JSON object keyed by day code, carrying only the working days:
/// <c>{"sat":{"from":"08:00","to":"16:00"},"sun":{…}}</c>. It reaches the server as a
/// <see cref="JsonElement"/> from the API and as raw JSON text when read back out of the
/// submissions column, so both shapes are handled here rather than at each caller.
///
/// The rendered line is what a submission viewer or an export shows, so it can be read without
/// going back to the form schema.
/// </summary>
public static class FormCalendarWithHours
{
    /// <summary>Between two days on the one line a human reads.</summary>
    private const string EnglishSeparator = ", ";
    private const string ArabicSeparator = "، ";

    /// <summary>Between the two ends of a shift. En dash — a range, not a subtraction.</summary>
    private const string RangeSeparator = "–";

    private const string FromProperty = "from";
    private const string ToProperty = "to";

    /// <summary>Guards against a malformed answer bloating a stored summary.</summary>
    private const int MaxDays = 7;

    /// <summary>
    /// The days a calendar-with-hours answer may carry, in the order they are rendered. Saturday first —
    /// the Saudi working week. Mirrors <c>WEEKDAY_CODES</c> in the web app's
    /// <c>formly-preview.types.ts</c>; the two must stay in sync, since these are the keys the
    /// client writes.
    /// </summary>
    private static readonly string[] DayCodes = ["sat", "sun", "mon", "tue", "wed", "thu", "fri"];

    /// <summary>
    /// Day names for the rendered line. Held here rather than resolved from a resource file for the
    /// same reason <see cref="FormGeolocation"/> formats its own point: the summary is written once
    /// at fill time and read by every client, so it cannot depend on the reader's language settings.
    /// </summary>
    private static readonly Dictionary<string, string> EnglishNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sat"] = "Saturday",
        ["sun"] = "Sunday",
        ["mon"] = "Monday",
        ["tue"] = "Tuesday",
        ["wed"] = "Wednesday",
        ["thu"] = "Thursday",
        ["fri"] = "Friday",
    };

    private static readonly Dictionary<string, string> ArabicNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sat"] = "السبت",
        ["sun"] = "الأحد",
        ["mon"] = "الاثنين",
        ["tue"] = "الثلاثاء",
        ["wed"] = "الأربعاء",
        ["thu"] = "الخميس",
        ["fri"] = "الجمعة",
    };

    /// <summary>
    /// Reads <paramref name="value"/> as the days it carries, in <see cref="DayCodes"/> order. A day
    /// missing either end is dropped: the client blocks a half-filled row before submit, and a value
    /// from anywhere else is not worth rendering half of.
    /// </summary>
    public static IReadOnlyList<FormCalendarDay> Read(object? value)
    {
        if (!TryReadObject(value, out var element))
        {
            return [];
        }

        var days = new List<FormCalendarDay>(MaxDays);

        foreach (var code in DayCodes)
        {
            if (!element.TryGetProperty(code, out var range) || range.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var from = ReadText(range, FromProperty);
            var to = ReadText(range, ToProperty);

            if (!string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(to))
            {
                days.Add(new FormCalendarDay(code, from, to));
            }
        }

        return days;
    }

    /// <summary>
    /// Renders <paramref name="value"/> as the one line a human reads, per language —
    /// <c>"Saturday 08:00–16:00, Sunday 09:00–17:00"</c>. Returns <see langword="false"/>, leaving
    /// both strings empty, for anything that carries no readable day.
    /// </summary>
    public static bool TryFormat(object? value, out string english, out string arabic)
    {
        var days = Read(value);

        if (days.Count == 0)
        {
            english = string.Empty;
            arabic = string.Empty;
            return false;
        }

        english = string.Join(EnglishSeparator, days.Select(day => Describe(day, EnglishNames)));
        arabic = string.Join(ArabicSeparator, days.Select(day => Describe(day, ArabicNames)));
        return true;
    }

    /// <summary>A day the map does not name falls back to its own code rather than disappearing.</summary>
    private static string Describe(in FormCalendarDay day, Dictionary<string, string> names)
    {
        var name = names.TryGetValue(day.Day, out var localized) ? localized : day.Day;
        return $"{name} {day.From}{RangeSeparator}{day.To}";
    }

    /// <summary>
    /// Unwraps the two shapes an answer arrives in: the object itself from the API, and that object's
    /// JSON text read back out of the <c>NVARCHAR</c> column.
    /// </summary>
    private static bool TryReadObject(object? value, out JsonElement element)
    {
        element = default;

        switch (value)
        {
            case null:
                return false;

            case JsonElement json when json.ValueKind == JsonValueKind.Object:
                element = json;
                return true;

            case JsonElement json when json.ValueKind == JsonValueKind.String:
                return TryParse(json.GetString(), out element);

            case string text:
                return TryParse(text, out element);

            default:
                return false;
        }
    }

    private static bool TryParse(string? raw, out JsonElement element)
    {
        element = default;

        if (string.IsNullOrWhiteSpace(raw) || !raw.TrimStart().StartsWith('{'))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            // Cloned because the document is disposed on the way out of this method.
            element = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ReadText(JsonElement range, string property) =>
        range.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
