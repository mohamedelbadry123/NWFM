namespace Tasks.Domain.Constants;

/// <summary>
/// The operation status C2M accepts on a field-activity closure, and the built-in rule for an
/// <c>Action Taken</c> answer nothing else accounts for. Ported from the reference app.
/// </summary>
/// <remarks>
/// C2M only understands two outcomes. Which one a task produces is decided by the option the crew
/// picked on <see cref="C2mFieldNames.ActionTaken"/>: the option itself when it names a status, else
/// the C2M action mapping table, else this rule — <see cref="CompletedActionCode"/> completes, every
/// other code cancels carrying itself as the reason. Closing an activity we cannot account for as
/// done is the more damaging mistake, so the fallback leans to cancelled.
/// </remarks>
public static class C2mOperationStatuses
{
    /// <summary>Work completed.</summary>
    public const string Completed = "C";

    /// <summary>Work not completed; the request carries a cancel reason.</summary>
    public const string Cancelled = "X";

    /// <summary>The one <c>Action Taken</c> code the built-in rule treats as done (WFM's <c>LKPIDOLD</c>).</summary>
    public const string CompletedActionCode = "OCUL01";

    public static readonly IReadOnlyList<string> All = [Completed, Cancelled];

    public static bool IsDefined(string? value) =>
        value is not null && All.Contains(value, StringComparer.Ordinal);

    public static string StatusForAction(string? actionCode) =>
        string.Equals(actionCode?.Trim(), CompletedActionCode, StringComparison.OrdinalIgnoreCase) ? Completed : Cancelled;

    public static string? CancelReasonForAction(string? actionCode) =>
        StatusForAction(actionCode) == Completed || string.IsNullOrWhiteSpace(actionCode) ? null : actionCode.Trim();
}

/// <summary>The form fields the C2M closure reads by name.</summary>
public static class C2mFieldNames
{
    /// <summary>Whose answer decides <c>FAStatus</c> and the reason. Its options may carry <c>c2m_fa_status</c>/<c>c2m_reason</c>.</summary>
    public const string ActionTaken = "wfm_action_taken";

    /// <summary>Sent as the closure's <c>comment</c>.</summary>
    public const string Remarks = "wfm_remarks";
}

/// <summary>The outcome of one attempt to close a field activity in C2M, as the dispatch log stores it.</summary>
public static class C2mDispatchStatuses
{
    /// <summary>Row written, call not yet answered.</summary>
    public const string Pending = "PENDING";

    public const string Succeeded = "SUCCEEDED";
    public const string Failed = "FAILED";

    /// <summary>The integration was off or bypassed, so nothing was sent.</summary>
    public const string Skipped = "SKIPPED";
}

/// <summary>
/// Where closing a task's field activity in C2M stands, on the task itself — the worklist and the
/// background sender read it without walking the dispatch log. Null on a task that closes nothing.
/// </summary>
public static class C2mClosureStatuses
{
    /// <summary>Approved, and waiting for the background sender (or its next retry).</summary>
    public const string Pending = "PENDING";

    /// <summary>C2M accepted the closure.</summary>
    public const string Closed = "CLOSED";

    /// <summary>Nothing was sent: the integration is off, or closing is bypassed.</summary>
    public const string Skipped = "SKIPPED";

    /// <summary>C2M answered and refused. Re-sending the same answers will not change that.</summary>
    public const string Rejected = "REJECTED";

    /// <summary>C2M could not be reached, and the retries ran out.</summary>
    public const string Failed = "FAILED";

    public static readonly IReadOnlyList<string> All = [Pending, Closed, Skipped, Rejected, Failed];

    /// <summary>Whether a person may send the closure again by hand.</summary>
    public static bool CanRetry(string? status) => status is Rejected or Failed;
}
