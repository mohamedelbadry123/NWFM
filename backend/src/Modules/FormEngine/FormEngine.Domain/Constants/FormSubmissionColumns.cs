namespace FormEngine.Domain.Constants;

/// <summary>
/// The fixed columns every row of the shared <c>FE.Submissions</c> table carries, whatever form
/// wrote it. Everything else on a row is a form field keyed by its <c>data_name</c>, so this list
/// is what separates a submission's metadata from its answers — and a field may never take one of
/// these names.
/// </summary>
public static class FormSubmissionColumns
{
    public const string Id = "Id";
    public const string FormDefinitionId = "FormDefinitionId";
    public const string VersionNo = "VersionNo";
    public const string Status = "Status";

    /// <summary>
    /// What owns the submission, e.g. <c>WorkItem</c> for a workflow task. Null for a stand-alone
    /// fill. Always set together with <see cref="ContextId"/>.
    /// </summary>
    public const string ContextType = "ContextType";

    public const string ContextId = "ContextId";
    public const string SubmittedBy = "SubmittedBy";
    public const string SubmittedByName = "SubmittedByName";
    public const string SubmittedDate = "SubmittedDate";
    public const string Created = "Created";

    /// <summary>
    /// The client's own key for the fill. Uniquely indexed per form, so replaying a submission after
    /// a dropped connection cannot write it a second time.
    /// </summary>
    public const string ClientSubmissionId = "ClientSubmissionId";

    public static readonly IReadOnlyList<string> All =
    [
        Id,
        FormDefinitionId,
        VersionNo,
        Status,
        ContextType,
        ContextId,
        SubmittedBy,
        SubmittedByName,
        SubmittedDate,
        Created,
        ClientSubmissionId
    ];

    /// <summary>Whether a column name is metadata rather than an answer.</summary>
    public static bool IsBase(string? column) =>
        column is not null && All.Contains(column, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Values written to <c>FE.Submissions.Status</c>.</summary>
public static class FormSubmissionStatuses
{
    public const string Submitted = "Submitted";
}
