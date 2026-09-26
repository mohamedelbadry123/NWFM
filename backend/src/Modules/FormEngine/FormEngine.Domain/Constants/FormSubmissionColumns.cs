namespace FormEngine.Domain.Constants;

/// <summary>
/// The fixed columns every per-form submission table carries. Everything else on a row is a form
/// field keyed by its <c>data_name</c>, so this list is what separates a submission's metadata from
/// its answers — and a field may never take one of these names.
/// </summary>
public static class FormSubmissionColumns
{
    public const string Id = "Id";

    /// <summary>
    /// Not a column: the table itself is the form. Read results still carry this key — filled from the
    /// request — so a row read on its own says which form it answers, and a field may not claim it.
    /// </summary>
    public const string FormDefinitionId = "FormDefinitionId";

    public const string VersionNo = "VersionNo";
    public const string Status = "Status";

    /// <summary>
    /// What owns the submission, e.g. <c>Task</c> for a field task. Null for a stand-alone fill.
    /// Always set together with <see cref="ContextId"/>.
    /// </summary>
    public const string ContextType = "ContextType";

    public const string ContextId = "ContextId";
    public const string SubmittedBy = "SubmittedBy";
    public const string SubmittedByName = "SubmittedByName";
    public const string SubmittedDate = "SubmittedDate";
    public const string Created = "Created";

    /// <summary>
    /// The client's own key for the fill. Uniquely indexed, so replaying a submission after a dropped
    /// connection cannot write it a second time.
    /// </summary>
    public const string ClientSubmissionId = "ClientSubmissionId";

    /// <summary>The columns that physically exist on every submission table.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Id,
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

    /// <summary>Names a field may not take: the physical columns, plus the form id every read result carries.</summary>
    private static readonly IReadOnlyList<string> Reserved = [.. All, FormDefinitionId];

    /// <summary>Whether a name is metadata rather than an answer.</summary>
    public static bool IsBase(string? column) =>
        column is not null && Reserved.Contains(column, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Values written to a submission row's <c>Status</c>.</summary>
public static class FormSubmissionStatuses
{
    public const string Submitted = "Submitted";
}
