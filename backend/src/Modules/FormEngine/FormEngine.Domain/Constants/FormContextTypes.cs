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

    /// <summary>
    /// Contexts only their owning module may write, in-process through <c>IFormGateway</c> after its
    /// own checks. The form engine's public submit endpoint refuses them: a fill posted there as
    /// <c>Task</c> would show up as that task's fill without the task ever having been asked.
    /// Workflow work item fills also go through their owning module's authorization checks.
    /// </summary>
    public static readonly IReadOnlySet<string> OwnedByModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Task,
        WorkItem,
    };
}
