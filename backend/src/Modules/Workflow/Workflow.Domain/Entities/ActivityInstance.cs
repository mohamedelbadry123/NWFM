namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using NWFM.Shared.MultiTenancy;
using Workflow.Domain.Enums;

/// <summary>
/// Tracks one execution slot for a single activity within a WorkflowInstance.
/// One row per activity per instance (re-try creates a new row).
/// </summary>
public sealed class ActivityInstance : Entity, ITenantAware
{
    public Guid OrganizationId { get; private set; }
    public Guid WorkflowInstanceId { get; private set; }
    public string ActivityNodeKey { get; private set; } = string.Empty;
    public ActivityType ActivityType { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public ActivityInstanceStatus Status { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? FailedAt { get; private set; }
    public string? FailureReason { get; private set; }
    public Guid? ExecutionTokenId { get; private set; }
    public void AttachToken(Guid? tokenId) => ExecutionTokenId = tokenId;
    public void Reopen() { Status = ActivityInstanceStatus.Active; FailedAt = null; FailureReason = null; }

    private ActivityInstance() { }

    public static ActivityInstance Start(
        Guid organizationId,
        Guid workflowInstanceId,
        string activityNodeKey,
        ActivityType activityType,
        string name,
        DateTime startedAt)
    {
        return new ActivityInstance
        {
            Id                 = Guid.NewGuid(),
            OrganizationId     = organizationId,
            WorkflowInstanceId = workflowInstanceId,
            ActivityNodeKey    = activityNodeKey,
            ActivityType       = activityType,
            Name               = name,
            Status             = ActivityInstanceStatus.Active,
            StartedAt          = startedAt,
            CreatedAt          = startedAt,
        };
    }

    public void Complete(DateTime completedAt)
    {
        Status      = ActivityInstanceStatus.Completed;
        CompletedAt = completedAt;
        SetUpdated(completedAt);
    }

    public void Skip(DateTime skippedAt)
    {
        Status      = ActivityInstanceStatus.Skipped;
        CompletedAt = skippedAt;
        SetUpdated(skippedAt);
    }

    public void Fail(string reason, DateTime failedAt)
    {
        Status        = ActivityInstanceStatus.Failed;
        FailureReason = reason;
        FailedAt      = failedAt;
        SetUpdated(failedAt);
    }
}
