namespace Tasks.Domain.Constants;

/// <summary>
/// A task's lifecycle. Values are stored as-is, uppercase, the way the reference survey stored its own.
/// <code>
/// CREATED → ASSIGNED → IN_PROGRESS → SUBMITTED → APPROVED
///                                       ↓
///                                    RETURNED → refill → SUBMITTED
/// any open state → EXPIRED
/// </code>
/// </summary>
public static class TaskStatuses
{
    public const string Created = "CREATED";
    public const string Assigned = "ASSIGNED";
    public const string InProgress = "IN_PROGRESS";
    public const string Submitted = "SUBMITTED";
    public const string Approved = "APPROVED";
    public const string Returned = "RETURNED";
    public const string Expired = "EXPIRED";

    public const int MaxLength = 30;

    public static readonly IReadOnlyList<string> All =
        [Created, Assigned, InProgress, Submitted, Approved, Returned, Expired];

    /// <summary>Before any fill: where a task can still be moved, re-pinned or handed to another team.</summary>
    public static readonly IReadOnlyList<string> Unfilled = [Created, Assigned, InProgress];

    /// <summary>Finished for good: nothing more is recorded against the task.</summary>
    public static bool IsClosed(string status) => status is Approved or Expired;

    public static bool IsDefined(string? status) =>
        status is not null && All.Contains(status, StringComparer.Ordinal);
}

/// <summary>States of one team's assignment to a task.</summary>
public static class TaskAssignmentStatuses
{
    public const string Pending = "PENDING";
    public const string InProgress = "IN_PROGRESS";
    public const string Submitted = "SUBMITTED";
    public const string Approved = "APPROVED";
    public const string Returned = "RETURNED";
    public const string Expired = "EXPIRED";

    /// <summary>Superseded by a later assignment of the same task.</summary>
    public const string Reassigned = "REASSIGNED";

    public const int MaxLength = 30;
}

public static class TaskPriorities
{
    public const string Low = "LOW";
    public const string Normal = "NORMAL";
    public const string High = "HIGH";
    public const string Urgent = "URGENT";

    public const int MaxLength = 20;

    public static readonly IReadOnlyList<string> All = [Low, Normal, High, Urgent];

    public static bool IsDefined(string? priority) =>
        priority is not null && All.Contains(priority, StringComparer.Ordinal);
}

/// <summary>Where a task came from.</summary>
public static class TaskSources
{
    public const string Manual = "MANUAL";
    public const string Api = "API";
    public const string Import = "IMPORT";

    public const int MaxLength = 20;

    public static readonly IReadOnlyList<string> All = [Manual, Api, Import];

    public static bool IsDefined(string? source) =>
        source is not null && All.Contains(source, StringComparer.Ordinal);
}

/// <summary>Why a reviewer sends a fill back. A fixed list, so returns can be counted by reason.</summary>
public static class TaskReturnReasons
{
    public const string IncompleteData = "INCOMPLETE_DATA";
    public const string WrongLocation = "WRONG_LOCATION";
    public const string PoorMedia = "POOR_MEDIA";
    public const string NeedsRevisit = "NEEDS_REVISIT";
    public const string Other = "OTHER";

    public const int MaxLength = 50;

    public static readonly IReadOnlyList<string> All = [IncompleteData, WrongLocation, PoorMedia, NeedsRevisit, Other];

    public static bool IsDefined(string? reason) =>
        reason is not null && All.Contains(reason, StringComparer.Ordinal);
}
