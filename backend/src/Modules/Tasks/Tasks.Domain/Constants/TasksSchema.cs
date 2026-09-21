namespace Tasks.Domain.Constants;

/// <summary>The SQL schema and table names owned by the Tasks module.</summary>
public static class TasksSchema
{
    public const string Name = "TK";

    public const string TaskTypes = "TaskTypes";
    public const string Tasks = "Tasks";
    public const string TaskAssignments = "TaskAssignments";
    public const string TaskStatusHistory = "TaskStatusHistory";

    /// <summary>EF migration history lives inside the module schema, like FormEngine's.</summary>
    public const string MigrationsHistoryTable = "__EFMigrationsHistory";

    /// <summary>What a task's fills are filed under in the form engine (<c>ContextType</c>).</summary>
    public const string FormContextType = "Task";
}
