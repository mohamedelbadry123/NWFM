namespace Workflow.Domain.Entities;

using NWFM.Shared.MultiTenancy;
using Workflow.Domain.Enums;

/// <summary>
/// Immutable append-only audit trail for every state transition in a WorkflowInstance.
/// Rows are never updated or deleted — analogous to ConsentTransaction.
/// </summary>
public sealed class WorkflowEvent : ITenantAware
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid WorkflowInstanceId { get; private set; }
    public WorkflowEventType EventType { get; private set; }
    public string? ActivityNodeKey { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string? PayloadJson { get; private set; }
    public DateTime OccurredAt { get; private set; }

    private WorkflowEvent() { }

    public static WorkflowEvent Append(
        Guid organizationId,
        Guid workflowInstanceId,
        WorkflowEventType eventType,
        DateTime occurredAt,
        string? activityNodeKey = null,
        Guid? actorUserId = null,
        string? payloadJson = null)
    {
        return new WorkflowEvent
        {
            Id                 = Guid.NewGuid(),
            OrganizationId     = organizationId,
            WorkflowInstanceId = workflowInstanceId,
            EventType          = eventType,
            ActivityNodeKey    = activityNodeKey,
            ActorUserId        = actorUserId,
            PayloadJson        = payloadJson,
            OccurredAt         = occurredAt,
        };
    }
}
