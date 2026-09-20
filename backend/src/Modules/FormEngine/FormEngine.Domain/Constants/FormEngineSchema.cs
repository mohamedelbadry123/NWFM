namespace FormEngine.Domain.Constants;

/// <summary>
/// The SQL schema and table names owned by the FormEngine module. <see cref="Submissions"/> is the
/// one table outside EF: it is created and widened by native SQL whenever a form is published.
/// </summary>
public static class FormEngineSchema
{
    public const string Name = "FE";

    public const string FormDefinitions = "FormDefinitions";
    public const string FormVersions = "FormVersions";
    public const string FieldCatalog = "FieldCatalog";
    public const string SubmissionFiles = "SubmissionFiles";
    public const string Submissions = "Submissions";

    /// <summary>EF migration history lives inside the module schema, like Workflow's.</summary>
    public const string MigrationsHistoryTable = "__EFMigrationsHistory";
}
