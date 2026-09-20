namespace FormEngine.Domain.Constants;

/// <summary>
/// Known owners of a submission (<c>FE.Submissions.ContextType</c>). The column is free text so a
/// new integration does not need a FormEngine change; these are the values NWFM itself writes.
/// </summary>
public static class FormContextTypes
{
    /// <summary>A workflow user task — <c>ContextId</c> holds the work item id.</summary>
    public const string WorkItem = "WorkItem";

    public const int MaxLength = 100;
}
