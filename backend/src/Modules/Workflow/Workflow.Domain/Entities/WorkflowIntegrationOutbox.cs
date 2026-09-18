namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using NWFM.Shared.MultiTenancy;
using Workflow.Domain.Enums;

/// <summary>
/// Reliable outbox for workflow outcome messages consumed by owning business modules.
/// </summary>
public sealed class WorkflowIntegrationOutbox : Entity, ITenantAware
{
    public Guid OrganizationId { get; private set; }
    public string MessageId { get; private set; } = string.Empty;
    public string? CorrelationId { get; private set; }
    public Guid BindingId { get; private set; }
    public Guid WorkflowInstanceId { get; private set; }
    public string ModuleKey { get; private set; } = string.Empty;
    public string BusinessEntityType { get; private set; } = string.Empty;
    public string BusinessEntityId { get; private set; } = string.Empty;
    public string OutcomeKey { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = "{}";
    public WorkflowOutboxStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime? PublishedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private WorkflowIntegrationOutbox() { }

    public static WorkflowIntegrationOutbox Create(
        Guid organizationId,
        string messageId,
        Guid bindingId,
        Guid workflowInstanceId,
        string moduleKey,
        string businessEntityType,
        string businessEntityId,
        string outcomeKey,
        string payloadJson,
        DateTime createdAt,
        string? correlationId = null)
    {
        return new WorkflowIntegrationOutbox
        {
            Id                 = Guid.NewGuid(),
            OrganizationId     = organizationId,
            MessageId          = messageId,
            CorrelationId      = correlationId,
            BindingId          = bindingId,
            WorkflowInstanceId = workflowInstanceId,
            ModuleKey          = moduleKey,
            BusinessEntityType = businessEntityType,
            BusinessEntityId   = businessEntityId,
            OutcomeKey         = outcomeKey,
            PayloadJson        = payloadJson,
            Status             = WorkflowOutboxStatus.Pending,
            AttemptCount       = 0,
            CreatedAt          = createdAt,
        };
    }

    public void MarkPublished(DateTime publishedAt)
    {
        Status      = WorkflowOutboxStatus.Published;
        PublishedAt = publishedAt;
        AttemptCount += 1;
        SetUpdated(publishedAt);
    }

    public void MarkFailed(DateTime failedAt)
    {
        Status = WorkflowOutboxStatus.Failed;
        AttemptCount += 1;
        SetUpdated(failedAt);
    }

    /// <summary>Re-queues a Failed outbox message for another publish attempt.</summary>
    public void ResetForReplay(DateTime resetAt)
    {
        if (Status != WorkflowOutboxStatus.Failed)
            throw new InvalidOperationException("Only Failed outbox messages can be replayed.");

        Status       = WorkflowOutboxStatus.Pending;
        AttemptCount = 0;
        PublishedAt  = null;
        SetUpdated(resetAt);
    }
}
