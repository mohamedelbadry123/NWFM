namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using NWFM.Shared.Exceptions;
using NWFM.Shared.MultiTenancy;
using NWFM.Shared.Organization;
using Workflow.Domain.Enums;

/// <summary>
/// A running (or completed) execution of a workflow version for a specific business entity
/// within a tenant. Pinned to a single Published version at start time — never re-pinned.
/// Runtime never executes a Draft version.
/// </summary>
public sealed class WorkflowInstance : Entity, ITenantAware
{
    public Guid OrganizationId { get; private set; }
    public Guid WorkflowBindingId { get; private set; }
    public Guid PinnedWorkflowVersionId { get; private set; }

    /// <summary>
    /// Composite deduplication key: sha256(bindingId + businessEntityId) stored as a string.
    /// Unique per organization to prevent duplicate starts for the same business event.
    /// </summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>Foreign key into the triggering module's entity (e.g. ConsentRequest.Id). Not a DB FK.</summary>
    public string BusinessEntityId { get; private set; } = string.Empty;

    /// <summary>Optional human-readable correlation identifier surfaced in the UI.</summary>
    public string? CorrelationId { get; private set; }

    public WorkflowInstanceStatus Status { get; private set; }

    public DateTime StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public DateTime? SuspendedAt { get; private set; }

    public string? FailureReason { get; private set; }

    /// <summary>Null for system-initiated instances (Consent shadow trigger).</summary>
    public Guid? StartedByUserId { get; private set; }

    /// <summary>NodeKey of the activity the engine is currently executing or has pending.</summary>
    public string CurrentActivityNodeKey { get; private set; } = string.Empty;

    /// <summary>When set, this instance was started by a CallActivity on the parent instance.</summary>
    public Guid? ParentInstanceId { get; private set; }

    /// <summary>NodeKey of the CallActivity on the parent that spawned this child.</summary>
    public string? ParentActivityNodeKey { get; private set; }
    public Guid? ParentActivityInstanceId { get; private set; }
    public const int OrgCodeMaxLength = 50;

    /// <summary>
    /// Where the work sits in the shared org hierarchy — the main workflow's location, copied into every
    /// child so the whole tree answers to one place. Null codes on instances started outside the workspace.
    /// </summary>
    public string? ClusterCode { get; private set; }
    public string? CbuCode { get; private set; }
    public string? BranchCode { get; private set; }
    public string? OperationAreaCode { get; private set; }
    public OrgLocation Location => new(ClusterCode, CbuCode, BranchCode, OperationAreaCode);
    public bool IsDemo { get; private set; }
    public void SetExecutionContext(OrgLocation? location, bool isDemo)
    {
        var place = location?.Normalized() ?? OrgLocation.Empty;
        if (new[] { place.ClusterCode, place.CbuCode, place.BranchCode, place.OperationAreaCode }.Any(c => c?.Length > OrgCodeMaxLength))
            throw new DomainException($"Organization codes cannot exceed {OrgCodeMaxLength} characters.");
        (ClusterCode, CbuCode, BranchCode, OperationAreaCode) = (place.ClusterCode, place.CbuCode, place.BranchCode, place.OperationAreaCode);
        IsDemo = isDemo;
    }
    public void AttachParentActivity(Guid? activityInstanceId) => ParentActivityInstanceId = activityInstanceId;

    public byte[] RowVersion { get; private set; } = [];

    private WorkflowInstance() { }

    public static WorkflowInstance Start(
        Guid organizationId,
        Guid workflowBindingId,
        Guid pinnedWorkflowVersionId,
        string idempotencyKey,
        string businessEntityId,
        string currentActivityNodeKey,
        DateTime startedAt,
        string? correlationId = null,
        Guid? startedByUserId = null,
        Guid? parentInstanceId = null,
        string? parentActivityNodeKey = null)
    {
        return new WorkflowInstance
        {
            Id                       = Guid.NewGuid(),
            OrganizationId           = organizationId,
            WorkflowBindingId        = workflowBindingId,
            PinnedWorkflowVersionId  = pinnedWorkflowVersionId,
            IdempotencyKey           = idempotencyKey,
            BusinessEntityId         = businessEntityId,
            CorrelationId            = correlationId,
            Status                   = WorkflowInstanceStatus.Running,
            StartedAt                = startedAt,
            StartedByUserId          = startedByUserId,
            CurrentActivityNodeKey   = currentActivityNodeKey,
            ParentInstanceId         = parentInstanceId,
            ParentActivityNodeKey    = parentActivityNodeKey,
            CreatedAt                = startedAt,
        };
    }

    public void AdvanceTo(string nodeKey, DateTime updatedAt)
    {
        CurrentActivityNodeKey = nodeKey;
        SetUpdated(updatedAt);
    }

    public void Complete(DateTime completedAt)
    {
        Status      = WorkflowInstanceStatus.Completed;
        CompletedAt = completedAt;
        SetUpdated(completedAt);
    }

    public void Cancel(DateTime cancelledAt)
    {
        Status      = WorkflowInstanceStatus.Cancelled;
        CancelledAt = cancelledAt;
        SetUpdated(cancelledAt);
    }

    public void Suspend(DateTime suspendedAt)
    {
        Status      = WorkflowInstanceStatus.Suspended;
        SuspendedAt = suspendedAt;
        SetUpdated(suspendedAt);
    }

    public void Resume(DateTime updatedAt)
    {
        Status      = WorkflowInstanceStatus.Running;
        SuspendedAt = null;
        FailureReason = null;
        SetUpdated(updatedAt);
    }

    public void Fail(string reason, DateTime failedAt)
    {
        Status        = WorkflowInstanceStatus.Failed;
        FailureReason = reason;
        SetUpdated(failedAt);
    }
}
