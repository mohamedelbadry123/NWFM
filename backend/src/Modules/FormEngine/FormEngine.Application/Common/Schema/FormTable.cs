namespace FormEngine.Application.Common.Schema;

/// <summary>
/// Everything the submission store needs to address one form's table: which form, the table's name,
/// and the type each of its answer columns was created as. The field types are what a value is
/// coerced to on write and what a read may select, so a key no published version declared can
/// neither be written nor read back.
/// </summary>
/// <param name="FieldTypes">Column (<c>data_name</c>) → builder element type. Case-insensitive, as SQL Server column names are.</param>
public sealed record FormTable(Guid FormDefinitionId, string TableName, IReadOnlyDictionary<string, string> FieldTypes)
{
    public static FormTable Create(
        Guid formDefinitionId,
        string tableName,
        IEnumerable<KeyValuePair<string, string>> fieldTypes)
    {
        var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, type) in fieldTypes)
        {
            types.TryAdd(name, type);
        }

        return new FormTable(formDefinitionId, tableName, types);
    }
}
