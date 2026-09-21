using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tasks.Application.Common.Interfaces;
using Tasks.Domain.Constants;
using Tasks.Domain.Entities;

namespace Tasks.Application.C2m;

/// <summary>
/// When a task closes a field activity in C2M, and what an attempt leaves on the task. Shared by the
/// approval, a person's retry and the background sender, so the three can never disagree.
/// </summary>
public sealed class TaskC2mClosure(
    ITasksDbContext db,
    IC2mDispatcher dispatcher,
    IOptions<C2mOptions> options,
    TimeProvider clock)
{
    public C2mOptions Options => options.Value;

    /// <summary>
    /// Whether approving the task has to close an activity in C2M: it names one (an FA id), and its
    /// type's form is the closing form. Work raised in NWFM for its own sake closes nothing upstream.
    /// </summary>
    public async Task<bool> AppliesAsync(FieldTask task, CancellationToken ct) =>
        !string.IsNullOrWhiteSpace(task.FaId)
        && await db.TaskTypes.AsNoTracking().AnyAsync(t => t.Id == task.TaskTypeId && t.ClosesC2mActivity, ct);

    /// <param name="queueOnTransportFailure">
    /// True on the background path: C2M not answering leaves the closure queued for the next retry,
    /// until <see cref="C2mOptions.MaxAttempts"/>. False when a person is waiting — they get the answer
    /// and decide.
    /// </param>
    public async Task<C2mDispatchOutcome> SendAsync(
        FieldTask task,
        DateTime completionTime,
        TimeSpan? timeout,
        bool queueOnTransportFailure,
        CancellationToken ct)
    {
        var outcome = await dispatcher.DispatchAsync(task, completionTime, timeout, ct);

        var status = outcome.Kind switch
        {
            C2mDispatchOutcomeKind.Accepted or C2mDispatchOutcomeKind.AlreadyAcknowledged => C2mClosureStatuses.Closed,
            C2mDispatchOutcomeKind.Skipped => C2mClosureStatuses.Skipped,
            C2mDispatchOutcomeKind.Rejected => C2mClosureStatuses.Rejected,
            _ => queueOnTransportFailure && task.C2mAttempts + 1 < options.Value.MaxAttempts
                ? C2mClosureStatuses.Pending
                : C2mClosureStatuses.Failed,
        };

        task.RecordC2mAttempt(status, clock.GetUtcNow().UtcDateTime);
        return outcome;
    }
}
