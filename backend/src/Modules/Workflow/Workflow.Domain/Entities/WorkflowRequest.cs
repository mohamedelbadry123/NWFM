namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using NWFM.Shared.MultiTenancy;
using Workflow.Domain.Enums;

/// <summary>
/// User-facing request list projection. One row per workflow instance.
/// Updated by the runtime projector after start/advance/claim/complete.
/// </summary>
public sealed class WorkflowRequest : Entity, ITenantAware
{
    public Guid OrganizationId { get; private set; }
    public string RequestNumber { get; private set; } = string.Empty;
    public Guid WorkflowBindingId { get; private set; }
    public Guid WorkflowInstanceId { get; private set; }
    public string BusinessEntityType { get; private set; } = string.Empty;
    public string BusinessEntityId { get; private set; } = string.Empty;
    public string ServiceKey { get; private set; } = string.Empty;
    public string ServiceNameEn { get; private set; } = string.Empty;
    public string? ServiceNameAr { get; private set; }
    public string? ScreenKey { get; private set; }
    public string TriggerEventKey { get; private set; } = string.Empty;
    public DateTime RequestDate { get; private set; }
    public Guid? RequesterUserId { get; private set; }
    public WorkflowInstanceStatus Status { get; private set; }
    public Guid? CurrentActivityInstanceId { get; private set; }
    public string? CurrentActivityNameEn { get; private set; }
    public string? CurrentActivityNameAr { get; private set; }
    public Guid? OriginalAssignedGroupId { get; private set; }
    public Guid? CurrentAssignedGroupId { get; private set; }
    public int? CurrentTaskSlaMinutes { get; private set; }
    public DateTime? CurrentTaskDueAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public string? CorrelationId { get; private set; }
    public Guid? CurrentClaimedByUserId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private WorkflowRequest() { }

    public static WorkflowRequest Create(
        Guid organizationId,
        string requestNumber,
        Guid workflowBindingId,
        Guid workflowInstanceId,
        string businessEntityType,
        string businessEntityId,
        string serviceKey,
        string serviceNameEn,
        DateTime requestDate,
        WorkflowInstanceStatus status,
        string triggerEventKey,
        DateTime createdAt,
        string? serviceNameAr = null,
        string? screenKey = null,
        Guid? requesterUserId = null,
        string? correlationId = null)
    {
        return new WorkflowRequest
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            RequestNumber = requestNumber,
            WorkflowBindingId = workflowBindingId,
            WorkflowInstanceId = workflowInstanceId,
            BusinessEntityType = businessEntityType,
            BusinessEntityId = businessEntityId,
            ServiceKey = serviceKey,
            ServiceNameEn = serviceNameEn,
            ServiceNameAr = serviceNameAr,
            ScreenKey = screenKey,
            TriggerEventKey = triggerEventKey,
            RequestDate = requestDate,
            RequesterUserId = requesterUserId,
            Status = status,
            CorrelationId = correlationId,
            CreatedAt = createdAt,
        };
    }

    public void SyncCurrentTask(
        Guid? activityInstanceId,
        string? activityNameEn,
        string? activityNameAr,
        Guid? assignedGroupId,
        int? slaMinutes,
        DateTime? dueAtUtc,
        Guid? claimedByUserId,
        DateTime updatedAt)
    {
        CurrentActivityInstanceId = activityInstanceId;
        CurrentActivityNameEn = activityNameEn;
        CurrentActivityNameAr = activityNameAr;
        CurrentAssignedGroupId = assignedGroupId;
        if (OriginalAssignedGroupId is null && assignedGroupId is not null)
            OriginalAssignedGroupId = assignedGroupId;
        CurrentTaskSlaMinutes = slaMinutes;
        CurrentTaskDueAtUtc = dueAtUtc;
        CurrentClaimedByUserId = claimedByUserId;
        SetUpdated(updatedAt);
    }

    /// <summary>Refresh display/link metadata without changing runtime state, assignments or history.</summary>
    public void SyncServiceMetadata(string serviceKey, string nameEn, string? nameAr, string? screenKey, DateTime updatedAt)
    {
        ServiceKey = serviceKey;
        ServiceNameEn = nameEn;
        ServiceNameAr = nameAr;
        ScreenKey = screenKey;
        SetUpdated(updatedAt);
    }

    public void SyncStatus(WorkflowInstanceStatus status, DateTime? completedAtUtc, DateTime updatedAt)
    {
        Status = status;
        CompletedAtUtc = completedAtUtc;
        if (status is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Cancelled or WorkflowInstanceStatus.Failed)
        {
            CurrentTaskSlaMinutes = null;
            CurrentTaskDueAtUtc = null;
            CurrentClaimedByUserId = null;
        }
        SetUpdated(updatedAt);
    }

    public void SyncClaim(Guid? claimedByUserId, DateTime updatedAt)
    {
        CurrentClaimedByUserId = claimedByUserId;
        SetUpdated(updatedAt);
    }

    public void SyncAssignment(Guid assignmentGroupId, Guid? claimedByUserId, DateTime updatedAt)
    {
        CurrentAssignedGroupId = assignmentGroupId;
        if (OriginalAssignedGroupId is null)
            OriginalAssignedGroupId = assignmentGroupId;
        CurrentClaimedByUserId = claimedByUserId;
        SetUpdated(updatedAt);
    }
}
