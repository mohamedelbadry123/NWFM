namespace Workflow.Infrastructure.Services;

using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

internal sealed class WorkflowTimerService : IWorkflowTimerService
{
    private readonly IWorkflowTimerRepository _timers;

    public WorkflowTimerService(IWorkflowTimerRepository timers) => _timers = timers;

    public async Task<Result<WorkflowTimer>> ScheduleAsync(
        Guid organizationId,
        Guid workflowInstanceId,
        Guid activityInstanceId,
        WorkflowTimerType timerType,
        DateTime dueAtUtc,
        DateTime now,
        string? signalKey = null,
        CancellationToken cancellationToken = default)
    {
        if (timerType == WorkflowTimerType.ExternalSignal && string.IsNullOrWhiteSpace(signalKey))
            return Result.Failure<WorkflowTimer>(WorkflowErrors.Timer.SignalKeyRequired);

        var timer = WorkflowTimer.Create(
            organizationId,
            workflowInstanceId,
            activityInstanceId,
            timerType,
            dueAtUtc,
            now,
            signalKey);

        await _timers.AddAsync(timer, cancellationToken);
        return Result.Success(timer);
    }

    public async Task<Result> MarkFiredAsync(Guid timerId, DateTime now, CancellationToken cancellationToken = default)
    {
        var timer = await _timers.GetByIdAsync(timerId, cancellationToken);
        if (timer is null)
            return Result.Failure(WorkflowErrors.Timer.NotFound);

        if (timer.Status != WorkflowTimerStatus.Pending)
            return Result.Failure(WorkflowErrors.Timer.NotPending);

        timer.MarkFired(now);
        await _timers.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> CompleteAsync(Guid timerId, DateTime now, CancellationToken cancellationToken = default)
    {
        var timer = await _timers.GetByIdAsync(timerId, cancellationToken);
        if (timer is null)
            return Result.Failure(WorkflowErrors.Timer.NotFound);

        if (timer.Status != WorkflowTimerStatus.Fired)
            return Result.Failure(WorkflowErrors.Timer.NotFired);

        timer.Complete(now);
        await _timers.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> CancelAsync(Guid timerId, DateTime now, CancellationToken cancellationToken = default)
    {
        var timer = await _timers.GetByIdAsync(timerId, cancellationToken);
        if (timer is null)
            return Result.Failure(WorkflowErrors.Timer.NotFound);

        if (timer.Status is WorkflowTimerStatus.Completed or WorkflowTimerStatus.Cancelled or WorkflowTimerStatus.Failed)
            return Result.Failure(WorkflowErrors.Timer.AlreadyTerminal);

        timer.Cancel(now);
        await _timers.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
