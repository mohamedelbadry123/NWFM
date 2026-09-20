namespace FormEngine.Application.Common.Schema;

/// <summary>
/// Every name a schema can write to <c>FE.Submissions</c>, with the field type its column is built
/// from: one per field, plus a <c>&lt;data_name&gt;_other</c> companion for each choice field offering
/// "Other" (see <see cref="FormChoiceOther"/>), which holds free text and so is typed as <c>text</c>.
/// </summary>
/// <remarks>
/// The publisher, the submission store's write path and its read whitelist all come through here, so a
/// companion can never exist on one and not the others. Names that could not be a SQL identifier are
/// dropped — publish rejects them, and an older schema that slipped one through must not reach a
/// statement.
/// </remarks>
public static class FormWritableFields
{
    public static IEnumerable<(string Name, string FieldType)> Of(FormSchema schema)
    {
        foreach (var field in schema.Fields)
        {
            if (!FormDataName.IsValid(field.DataName))
            {
                continue;
            }

            yield return (field.DataName, field.FieldType);

            if (!FormChoiceOther.NeedsCompanion(field))
            {
                continue;
            }

            var companion = FormChoiceOther.KeyFor(field.DataName);
            if (FormDataName.IsValid(companion))
            {
                yield return (companion, FormElementTypes.Text);
            }
        }
    }
}
