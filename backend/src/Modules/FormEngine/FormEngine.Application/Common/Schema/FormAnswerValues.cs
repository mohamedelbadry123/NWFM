using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace FormEngine.Application.Common.Schema;

/// <summary>
/// One value read out of a posted answer set, carrying whether the key was there at all.
///
/// JavaScript draws a line the CLR does not: a missing key is <c>undefined</c> and an explicit null
/// is <c>null</c>, and the rule engine treats them differently — <c>Number(null)</c> is 0 while
/// <c>Number(undefined)</c> is NaN, so <c>less_than "5"</c> is true for a key posted as null and
/// false for a key that was never sent. The browser evaluates these rules that way, so the server
/// has to as well or the two disagree about which fields are required.
/// </summary>
public readonly record struct FormAnswerValue(object? Value, bool IsDefined)
{
    /// <summary>The key was not in the answer set at all — JavaScript's <c>undefined</c>.</summary>
    public static readonly FormAnswerValue Undefined = new(null, false);

    public static FormAnswerValue Of(object? value) => new(value, true);

    public static FormAnswerValue Read(IReadOnlyDictionary<string, object?> answers, string key) =>
        answers.TryGetValue(key, out var value) ? Of(value) : Undefined;
}

/// <summary>
/// JavaScript-faithful coercion of posted answers, shared by <see cref="FormRuleEngine"/> and
/// <see cref="FormAnswerValidator"/>.
///
/// The rules and the validators these back were written against the browser's semantics, in
/// `form-builder-rules.ts` and Angular's own validators. Anything that reads an answer differently
/// here — a trimmed string, a `Convert.ToDouble`, an array compared by reference — makes the server
/// reject fills the form told the user were valid, which is worse than the gap it closes.
///
/// Answers arrive off the JSON pipeline as <see cref="JsonElement"/> when the caller is an HTTP
/// client, and as plain CLR values when a handler builds them, so every helper accepts both.
/// </summary>
public static class FormAnswerValues
{
    /// <summary>JavaScript's <c>Number.NaN</c>, returned by <see cref="ToNumber"/> for what will not coerce.</summary>
    public const double NotANumber = double.NaN;

    /// <summary>
    /// Whether the answer counts as unanswered. Mirrors `isEmpty` in the rule engine and `isBlank`
    /// in the renderer: an absent key, null, the empty string, or an empty array.
    /// </summary>
    /// <remarks>
    /// Deliberately **not** trimmed. Angular's <c>Validators.required</c> accepts a single space, so
    /// trimming here would refuse a fill the form accepted. It does leave a space as a way past a
    /// required field; closing that means changing both sides together.
    /// <para>
    /// Note <c>0</c> and <c>false</c> are answers, not blanks — a meter reading of zero is a reading.
    /// </para>
    /// </remarks>
    public static bool IsBlank(FormAnswerValue answer)
    {
        if (!answer.IsDefined)
        {
            return true;
        }

        return answer.Value switch
        {
            null => true,
            string text => text.Length == 0,
            JsonElement json => IsBlankJson(json),
            _ => AsList(answer.Value) is { Count: 0 },
        };
    }

    public static bool IsBlank(object? value) => IsBlank(FormAnswerValue.Of(value));

    private static bool IsBlankJson(JsonElement json) => json.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => true,
        JsonValueKind.String => json.GetString()!.Length == 0,
        JsonValueKind.Array => json.GetArrayLength() == 0,
        _ => false,
    };

    /// <summary>
    /// The answer's elements when it is an array, else <c>null</c>. A string is never an array here,
    /// however enumerable the CLR considers it.
    /// </summary>
    public static IReadOnlyList<object?>? AsList(object? value)
    {
        switch (value)
        {
            case null or string:
                return null;

            case JsonElement { ValueKind: JsonValueKind.Array } json:
                return json.EnumerateArray().Select(item => (object?)item).ToList();

            case JsonElement:
                return null;

            case IEnumerable enumerable:
                return enumerable.Cast<object?>().ToList();

            default:
                return null;
        }
    }

    /// <summary>
    /// JavaScript's <c>String(value)</c>: null and undefined render as the empty string, an array
    /// joins its elements with commas, and a number renders without a trailing <c>.0</c>.
    /// </summary>
    public static string AsText(FormAnswerValue answer) =>
        answer.IsDefined ? AsText(answer.Value) : string.Empty;

    public static string AsText(object? value)
    {
        switch (value)
        {
            case null:
                return string.Empty;

            case string text:
                return text;

            case bool flag:
                return flag ? "true" : "false";

            case JsonElement json:
                return AsTextJson(json);
        }

        if (AsList(value) is { } items)
        {
            return string.Join(",", items.Select(AsText));
        }

        return value switch
        {
            double number => NumberToText(number),
            float number => NumberToText(number),
            decimal number => NumberToText((double)number),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static string AsTextJson(JsonElement json) => json.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        JsonValueKind.String => json.GetString() ?? string.Empty,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => NumberToText(json.GetDouble()),
        JsonValueKind.Array => string.Join(",", json.EnumerateArray().Select(item => AsText((object?)item))),
        // `String({})` is "[object Object]" in the browser; nothing the builder emits reaches this,
        // and any text would only ever be compared for equality, so the raw JSON is the honest answer.
        _ => json.GetRawText(),
    };

    /// <summary>Renders a number the way JavaScript does — <c>5</c>, not <c>5.0</c>.</summary>
    private static string NumberToText(double value)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }

        if (double.IsPositiveInfinity(value))
        {
            return "Infinity";
        }

        if (double.IsNegativeInfinity(value))
        {
            return "-Infinity";
        }

        return value == Math.Floor(value) && Math.Abs(value) < 1e21
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// JavaScript's <c>Number(value)</c>, including the parts that surprise: <c>undefined</c> is
    /// NaN but <c>null</c> is 0, the empty and all-whitespace strings are 0, <c>true</c> is 1, and an
    /// array coerces through its comma-joined text (so <c>[5]</c> is 5 and <c>["a","b"]</c> is NaN).
    /// </summary>
    public static double ToNumber(FormAnswerValue answer) =>
        answer.IsDefined ? ToNumber(answer.Value) : NotANumber;

    public static double ToNumber(object? value)
    {
        switch (value)
        {
            case null:
                return 0d;

            case bool flag:
                return flag ? 1d : 0d;

            case double number:
                return number;

            case float number:
                return number;

            case decimal number:
                return (double)number;

            case JsonElement json:
                return ToNumberJson(json);

            case IConvertible convertible and (sbyte or byte or short or ushort or int or uint or long or ulong):
                return convertible.ToDouble(CultureInfo.InvariantCulture);
        }

        // Strings, arrays and everything else coerce through their text, exactly as the browser does.
        return TextToNumber(AsText(value));
    }

    private static double ToNumberJson(JsonElement json) => json.ValueKind switch
    {
        JsonValueKind.Null => 0d,
        JsonValueKind.Undefined => NotANumber,
        JsonValueKind.True => 1d,
        JsonValueKind.False => 0d,
        JsonValueKind.Number => json.GetDouble(),
        _ => TextToNumber(AsTextJson(json)),
    };

    /// <summary>
    /// The string half of <c>Number()</c>. Whitespace is trimmed first and an empty result is 0;
    /// anything that is not a plain decimal number is NaN.
    /// </summary>
    /// <remarks>
    /// JavaScript also reads <c>0x10</c>, <c>Infinity</c> and binary/octal literals here. They are
    /// not supported: a rule's comparison value is typed into the builder by a form author, and none
    /// of those forms can be entered by accident.
    /// </remarks>
    private static double TextToNumber(string text)
    {
        var trimmed = text.Trim();

        if (trimmed.Length == 0)
        {
            return 0d;
        }

        return double.TryParse(
            trimmed,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : NotANumber;
    }
}
