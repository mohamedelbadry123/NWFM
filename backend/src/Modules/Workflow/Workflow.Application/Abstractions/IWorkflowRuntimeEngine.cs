namespace Workflow.Application.Abstractions;

using NWFM.Shared.Results;
using Workflow.Domain.Entities;

/// <summary>
/// Drives a WorkflowInstance forward from the current activity to the next steady state.
/// Handles Start, UserTask, ExclusiveGateway, ServiceTask, Timer, NotificationTask,
/// ParallelGateway, InclusiveGateway, JoinGateway, CallActivity, ScriptTask, WaitEvent, and End.
/// Only executes Published versions — caller must pre-validate.
/// </summary>
public interface IWorkflowRuntimeEngine
{
    Task<Result> CompleteExternalActivityAsync(Guid activityInstanceId, IReadOnlyDictionary<string, object?> outputs,
        string outcome, string? error, DateTime now, CancellationToken cancellationToken = default);
    /// <summary>
    /// Starts a new instance from the Start activity and advances until a UserTask or End is reached.
    /// Returns the created instance.
    /// </summary>
    Task<Result<WorkflowInstance>> StartAsync(
        Guid organizationId,
        Guid workflowBindingId,
        string businessEntityId,
        string idempotencyKey,
        DateTime now,
        string? correlationId = null,
        Guid? startedByUserId = null,
        Guid? parentInstanceId = null,
        string? parentActivityNodeKey = null,
        Guid? pinnedWorkflowVersionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Continues an existing instance after a WorkItem has been completed.
    /// Evaluates outgoing transitions and advances to the next steady state.
    /// </summary>
    Task<Result> AdvanceAsync(
        Guid workflowInstanceId,
        Guid completedWorkItemId,
        DateTime now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a workflow instance when a Pending timer becomes due.
    /// Idempotent if the timer is already Fired or Completed.
    /// </summary>
    Task<Result> ResumeFromTimerAsync(
        Guid timerId,
        DateTime now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a workflow instance waiting on a WaitEvent activity (external signal).
    /// Advances from the wait node via its single outgoing transition.
    /// </summary>
    Task<Result> ResumeFromExternalSignalAsync(
        Guid workflowInstanceId,
        string? signalKey,
        DateTime now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a parent instance waiting on a CallActivity after the child completes.
    /// </summary>
    Task<Result> ResumeFromCallActivityAsync(
        Guid parentInstanceId,
        string callActivityNodeKey,
        DateTime now,
        CancellationToken cancellationToken = default);
}
