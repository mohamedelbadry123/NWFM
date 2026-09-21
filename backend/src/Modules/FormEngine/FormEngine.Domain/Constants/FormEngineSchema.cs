namespace FormEngine.Domain.Constants;

/// <summary>
/// The SQL schema and table names owned by the FormEngine module. A form's submissions are the one
/// kind of table outside EF: each published form gets its own, named on first publish
/// (<see cref="SubmissionTablePrefix"/> + its code), and widened by native SQL as versions add fields.
/// </summary>
public static class FormEngineSchema
{
    public const string Name = "FE";

    public const string FormDefinitions = "FormDefinitions";
    public const string FormVersions = "FormVersions";
    public const string FormFields = "FormFields";
    public const string SubmissionFiles = "SubmissionFiles";

    /// <summary>Every per-form submission table starts with this, so they sort together and read as what they are.</summary>
    public const string SubmissionTablePrefix = "SUB_";

    /// <summary>
    /// The single shared table submissions used to live in, before each form had its own. Named only so
    /// the migration that retired it can drop it.
    /// </summary>
    public const string LegacySharedSubmissions = "Submissions";

    /// <summary>EF migration history lives inside the module schema, like Workflow's.</summary>
    public const string MigrationsHistoryTable = "__EFMigrationsHistory";
}
