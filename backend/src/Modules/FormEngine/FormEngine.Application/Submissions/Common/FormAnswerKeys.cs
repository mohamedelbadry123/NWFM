namespace FormEngine.Application.Submissions.Common;

/// <summary>
/// Brings a posted answer dictionary onto the same keys the schema is read under.
///
/// A <c>data_name</c> is trimmed when the schema is parsed, so a design carrying <c>"leak_type "</c>
/// yields the field — and the column — <c>leak_type</c>, while a client keys its payload on the raw
/// name it was given. Every consumer downstream (the submission store, the media linker, the
/// signature normaliser) matches answers against the parsed field set, so an untrimmed key matches
/// none of them and the answer is dropped in silence.
///
/// Normalising once, at the slice boundary, is what keeps those consumers agreeing with each other.
/// </summary>
internal static class FormAnswerKeys
{
    /// <summary>
    /// The answers re-keyed on their trimmed names. A key that needs no trimming is kept as it is,
    /// and where both forms were posted the trimmed one wins — it is the name that has a column.
    /// </summary>
    public static Dictionary<string, object?> Normalize(IReadOnlyDictionary<string, object?> answers)
    {
        var normalized = new Dictionary<string, object?>(answers.Count, StringComparer.Ordinal);

        foreach (var (key, value) in answers)
        {
            var trimmed = key.Trim();
            var wasExact = trimmed.Length == key.Length;

            if (wasExact || !normalized.ContainsKey(trimmed))
            {
                normalized[trimmed] = value;
            }
        }

        return normalized;
    }
}
