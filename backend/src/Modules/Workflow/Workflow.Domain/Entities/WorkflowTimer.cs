namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using NWFM.Shared.MultiTenancy;
using Workflow.Domain.Enums;

/// <summary>
/// Scheduled timer attached to a workflow/activity instance.
/// DueDate and Duration timers fire by DueAt; ExternalSignal waits for MarkFired via signal key.
/// </summary>
public sealed class WorkflowTimer : Entity, ITenantAware
{
    public Guid OrganizationId { get; private set; }
    public Guid WorkflowInstanceId { get; private set; }
    public Guid ActivityInstanceId { get; private set; }
    public WorkflowTimerType TimerType { get; private set; }
    public DateTime DueAt { get; private set; }
    public string? SignalKey { get; private set; }
    public WorkflowTimerStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime? LastAttemptAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private WorkflowTimer() { }

    public static WorkflowTimer Create(
        Guid organizationId,
        Guid workflowInstanceId,
        Guid activityInstanceId,
        WorkflowTimerType timerType,
        DateTime dueAt,
        DateTime createdAt,
        string? signalKey = null)
    {
        return new WorkflowTimer
        {
            Id                   = Guid.NewGuid(),
            OrganizationId       = organizationId,
            WorkflowInstanceId   = workflowInstanceId,
            ActivityInstanceId   = activityInstanceId,
            TimerType            = timerType,
            DueAt                = dueAt,
            SignalKey            = signalKey,
            Status               = WorkflowTimerStatus.Pending,
            AttemptCount         = 0,
            CreatedAt            = createdAt,
        };
    }

    public void MarkFired(DateTime firedAt)
    {
        Status        = WorkflowTimerStatus.Fired;
        AttemptCount += 1;
        LastAttemptAt = firedAt;
        SetUpdated(firedAt);
    }

    public void Complete(DateTime completedAt)
    {
        Status      = WorkflowTimerStatus.Completed;
        CompletedAt = completedAt;
        SetUpdated(completedAt);
    }

    public void Cancel(DateTime cancelledAt)
    {
        Status      = WorkflowTimerStatus.Cancelled;
        CancelledAt = cancelledAt;
        SetUpdated(cancelledAt);
    }

    public void Fail(DateTime failedAt)
    {
        Status        = WorkflowTimerStatus.Failed;
        AttemptCount += 1;
        LastAttemptAt = failedAt;
        SetUpdated(failedAt);
    }
}
