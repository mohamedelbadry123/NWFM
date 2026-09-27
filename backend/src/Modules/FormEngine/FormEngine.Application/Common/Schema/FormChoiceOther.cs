namespace FormEngine.Application.Common.Schema;

/// <summary>
/// The <c>allow_other</c> contract, mirrored from <c>formly-preview.types.ts</c> — the two must stay
/// in sync. A choice field offering "Other" keeps the sentinel as its own answer, so a visibility or
/// required rule can still match "the user chose Other" deterministically, and the typed free text
/// travels alongside it under a companion <c>&lt;data_name&gt;_other</c> key. That companion is a
/// column of its own, which is why the flag has to reach the submission store and the field catalog
/// rather than staying a client-side input concern.
/// </summary>
public static class FormChoiceOther
{
    /// <summary>The option value a field holds when the answer is free text.</summary>
    public const string Sentinel = "__other__";

    private const string KeySuffix = "_other";

    /// <summary>The companion answer key / column carrying a field's free text.</summary>
    public static string KeyFor(string dataName) => dataName + KeySuffix;

    /// <summary>
    /// True when <paramref name="field"/> offers "Other" and therefore needs a companion column.
    /// Guarded on the field type as well as the flag: the builder writes <c>allow_other</c> onto its
    /// element model generally, and only a choice field can ever produce the sentinel.
    /// </summary>
    public static bool NeedsCompanion(FormSchemaField field) =>
        field.AllowOther && FormAnswerDisplay.IsChoice(field.FieldType);

    /// <summary>
    /// The field <paramref name="key"/> is the companion of, or <c>null</c> when it is not a
    /// companion key of any field in <paramref name="fields"/>. Used where a companion has to be
    /// read back as part of the field it belongs to rather than as a nameless answer of its own.
    /// </summary>
    public static FormSchemaField? OwnerOf(
        string key,
        IReadOnlyDictionary<string, FormSchemaField> fields)
    {
        if (!key.EndsWith(KeySuffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var ownerName = key[..^KeySuffix.Length];

        return fields.TryGetValue(ownerName, out var owner) && NeedsCompanion(owner) ? owner : null;
    }
}
