namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using NWFM.Shared.MultiTenancy;
using Workflow.Domain.Enums;

/// <summary>
/// Reliable inbox for inbound module trigger messages consumed by the workflow engine.
/// Mirrors <see cref="WorkflowIntegrationOutbox"/> for the inbound path.
/// </summary>
public sealed class WorkflowIntegrationInbox : Entity, ITenantAware
{
    public Guid OrganizationId { get; private set; }
    public string MessageId { get; private set; } = string.Empty;
    public string? CorrelationId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string ModuleKey { get; private set; } = string.Empty;
    public string BusinessEntityType { get; private set; } = string.Empty;
    public string BusinessEntityId { get; private set; } = string.Empty;
    public string TriggerEvent { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = "{}";
    public WorkflowInboxStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime? ProcessedAt { get; private set; }
    public string? ErrorMessage { get; private set; }
    public Guid? BindingId { get; private set; }
    public Guid? WorkflowInstanceId { get; private set; }
    public string Operation { get; private set; } = "Start";
    public Guid? TargetActivityInstanceId { get; private set; }

    public void SetSignalTarget(Guid instanceId, Guid? activityInstanceId)
    { Operation = "Signal"; WorkflowInstanceId = instanceId; TargetActivityInstanceId = activityInstanceId; }
    public byte[] RowVersion { get; private set; } = [];

    private WorkflowIntegrationInbox() { }

    public static WorkflowIntegrationInbox Create(
        Guid organizationId,
        string messageId,
        string idempotencyKey,
        string moduleKey,
        string businessEntityType,
        string businessEntityId,
        string triggerEvent,
        string payloadJson,
        DateTime createdAt,
        string? correlationId = null,
        Guid? bindingId = null)
    {
        return new WorkflowIntegrationInbox
        {
            Id                 = Guid.NewGuid(),
            OrganizationId     = organizationId,
            MessageId          = messageId,
            CorrelationId      = correlationId,
            IdempotencyKey     = idempotencyKey,
            ModuleKey          = moduleKey,
            BusinessEntityType = businessEntityType,
            BusinessEntityId   = businessEntityId,
            TriggerEvent       = triggerEvent,
            PayloadJson        = payloadJson,
            Status             = WorkflowInboxStatus.Pending,
            AttemptCount       = 0,
            BindingId          = bindingId,
            CreatedAt          = createdAt,
        };
    }

    public void MarkProcessing(DateTime processingAt)
    {
        Status = WorkflowInboxStatus.Processing;
        AttemptCount += 1;
        SetUpdated(processingAt);
    }

    public void MarkProcessed(Guid workflowInstanceId, DateTime processedAt)
    {
        Status             = WorkflowInboxStatus.Processed;
        WorkflowInstanceId = workflowInstanceId;
        ProcessedAt        = processedAt;
        ErrorMessage       = null;
        SetUpdated(processedAt);
    }

    public void MarkFailed(string error, DateTime failedAt)
    {
        Status       = WorkflowInboxStatus.Failed;
        ErrorMessage = error;
        AttemptCount += 1;
        SetUpdated(failedAt);
    }

    public void MarkDeadLetter(DateTime deadLetteredAt, string? error = null)
    {
        Status = WorkflowInboxStatus.DeadLetter;
        if (error is not null)
            ErrorMessage = error;
        SetUpdated(deadLetteredAt);
    }

    /// <summary>Re-queues a DeadLetter (or Failed) message for another processing attempt.</summary>
    public void ResetForReplay(DateTime resetAt)
    {
        if (Status is not (WorkflowInboxStatus.DeadLetter or WorkflowInboxStatus.Failed))
            throw new InvalidOperationException("Only DeadLetter or Failed inbox messages can be replayed.");

        Status       = WorkflowInboxStatus.Pending;
        AttemptCount = 0;
        ErrorMessage = null;
        SetUpdated(resetAt);
    }
}
