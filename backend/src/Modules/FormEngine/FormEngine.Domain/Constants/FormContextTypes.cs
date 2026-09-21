namespace FormEngine.Domain.Constants;

/// <summary>
/// Known owners of a submission (a submission row's <c>ContextType</c>). The column is free text so a
/// new integration does not need a FormEngine change; these are the values NWFM itself writes.
/// </summary>
public static class FormContextTypes
{
    /// <summary>A workflow user task — <c>ContextId</c> holds the work item id.</summary>
    public const string WorkItem = "WorkItem";

    /// <summary>A field task — <c>ContextId</c> holds the task id.</summary>
    public const string Task = "Task";

    public const int MaxLength = 100;
}
