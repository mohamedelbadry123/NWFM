namespace Workflow.Application.Abstractions;

using NWFM.Shared.Results;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

/// <summary>
/// Schedules and advances workflow timers. Stub for advanced engine integration.
/// </summary>
public interface IWorkflowTimerService
{
    Task<Result<WorkflowTimer>> ScheduleAsync(
        Guid organizationId,
        Guid workflowInstanceId,
        Guid activityInstanceId,
        WorkflowTimerType timerType,
        DateTime dueAtUtc,
        DateTime now,
        string? signalKey = null,
        CancellationToken cancellationToken = default);

    Task<Result> MarkFiredAsync(
        Guid timerId,
        DateTime now,
        CancellationToken cancellationToken = default);

    Task<Result> CompleteAsync(
        Guid timerId,
        DateTime now,
        CancellationToken cancellationToken = default);

    Task<Result> CancelAsync(
        Guid timerId,
        DateTime now,
        CancellationToken cancellationToken = default);
}
