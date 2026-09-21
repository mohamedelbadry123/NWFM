namespace FormEngine.Application.Common.Schema;

/// <summary>
/// The application locks that serialise changes to submission storage. Each is held for the length
/// of the transaction that takes it.
/// </summary>
public static class FormStorageLocks
{
    /// <summary>
    /// Held while submission table names are handed out. Two codes can collapse to one name, so the
    /// check for a free name and the claim on it must not interleave across forms.
    /// </summary>
    public const string TableNaming = "FE.SubmissionTables.Naming";

    /// <summary>
    /// Held while one form's table is altered. Scoped to the form: publishing a different form alters
    /// a different table and has no reason to wait.
    /// </summary>
    public static string Form(Guid formDefinitionId) => $"FE.Form.{formDefinitionId:N}";
}
