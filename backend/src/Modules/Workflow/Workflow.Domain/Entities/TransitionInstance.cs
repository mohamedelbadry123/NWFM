namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using NWFM.Shared.MultiTenancy;

/// <summary>
/// Immutable record of a transition taken during workflow execution.
/// Append-only — no update methods after create.
/// </summary>
public sealed class TransitionInstance : Entity, ITenantAware
{
    public Guid OrganizationId { get; private set; }
    public Guid WorkflowInstanceId { get; private set; }
    public string FromActivityNodeKey { get; private set; } = string.Empty;
    public string ToActivityNodeKey { get; private set; } = string.Empty;
    public string? TransitionKey { get; private set; }
    public string? ConditionExpression { get; private set; }
    public bool WasDefault { get; private set; }
    public DateTime TakenAt { get; private set; }
    public Guid? ActorUserId { get; private set; }

    private TransitionInstance() { }

    public static TransitionInstance Create(
        Guid organizationId,
        Guid workflowInstanceId,
        string fromActivityNodeKey,
        string toActivityNodeKey,
        DateTime takenAt,
        string? transitionKey = null,
        string? conditionExpression = null,
        bool wasDefault = false,
        Guid? actorUserId = null)
        => Append(
            organizationId,
            workflowInstanceId,
            fromActivityNodeKey,
            toActivityNodeKey,
            takenAt,
            transitionKey,
            conditionExpression,
            wasDefault,
            actorUserId);

    /// <summary>Append-only factory — equivalent to <see cref="Create"/>.</summary>
    public static TransitionInstance Append(
        Guid organizationId,
        Guid workflowInstanceId,
        string fromActivityNodeKey,
        string toActivityNodeKey,
        DateTime takenAt,
        string? transitionKey = null,
        string? conditionExpression = null,
        bool wasDefault = false,
        Guid? actorUserId = null)
    {
        return new TransitionInstance
        {
            Id                   = Guid.NewGuid(),
            OrganizationId       = organizationId,
            WorkflowInstanceId   = workflowInstanceId,
            FromActivityNodeKey  = fromActivityNodeKey,
            ToActivityNodeKey    = toActivityNodeKey,
            TransitionKey        = transitionKey,
            ConditionExpression  = conditionExpression,
            WasDefault           = wasDefault,
            TakenAt              = takenAt,
            ActorUserId          = actorUserId,
            CreatedAt            = takenAt,
        };
    }
}
