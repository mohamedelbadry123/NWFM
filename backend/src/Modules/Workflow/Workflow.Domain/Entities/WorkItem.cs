namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using NWFM.Shared.MultiTenancy;
using Workflow.Domain.Enums;

/// <summary>
/// A user-facing task created when the engine reaches a UserTask activity.
/// Exactly one WorkItem per ActivityInstance for UserTask activities.
/// Concurrent claim is protected by RowVersion optimistic concurrency.
/// </summary>
public sealed class WorkItem : Entity, ITenantAware
{
    public Guid OrganizationId { get; private set; }
    public Guid WorkflowInstanceId { get; private set; }
    public Guid ActivityInstanceId { get; private set; }

    /// <summary>The legacy assignment group. Empty for role assignments, whose recipients are recorded as Role candidates.</summary>
    public Guid AssignmentGroupId { get; private set; }

    public Guid? ClaimedByUserId { get; private set; }
    public DateTime? ClaimedAt { get; private set; }

    public Guid? CompletedByUserId { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    public DateTime? DueAt { get; private set; }

    public WorkItemStatus Status { get; private set; }

    /// <summary>The action key the user selected to complete the task (e.g. "Approve", "Reject").</summary>
    public string? ActionTaken { get; private set; }
    public string? CommentText { get; private set; }
    public string? FormDataJson { get; private set; }
    public string? FormProgressJson { get; private set; }
    // Private attempt entropy is never exposed in DTOs. A claim rolled back with the workflow
    // still derives the same receipt; a transfer/release starts a different immutable attempt.
    [System.Text.Json.Serialization.JsonIgnore]
    public Guid? FormReceiptAttempt { get; private set; }
    public Guid? CompletedFormReceiptKey { get; private set; }
    /// <summary>Opaque UI draft continuity only; never a submission receipt or authorization token.
    /// Claim does not change it. Ownership attempt rotation and a new form visit do.</summary>
    public Guid FormDraftKey(Guid visitId)
    {
        var attempt = FormReceiptAttempt?.ToString("N") ?? "legacy";
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            $"nwfm:form-draft:v1:{OrganizationId:N}:{Id:N}:{visitId:N}:{attempt}"));
        return new Guid(bytes.AsSpan(0, 16));
    }
    public Guid FormReceiptKey(Guid visitId, Guid actorId)
    {
        if (FormReceiptAttempt is not Guid attempt) return visitId; // Existing published/runtime receipts.
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{attempt:N}:{visitId:N}:{actorId:N}"));
        return new Guid(bytes.AsSpan(0, 16));
    }
    public void RecordFormReceipt(Guid key) => CompletedFormReceiptKey = key;
    public void SetFormProgress(string json) => FormProgressJson = json;
    public void SetFormData(string json) => FormDataJson = json;
    public void WaitForMapping() { if (Status is WorkItemStatus.Pending or WorkItemStatus.Claimed) Status = WorkItemStatus.Preparing; }
    public void MappingReady() { if (Status == WorkItemStatus.Preparing) Status = ClaimedByUserId is null ? WorkItemStatus.Pending : WorkItemStatus.Claimed; }

    /// <summary>Optimistic concurrency token — only one claimant wins a concurrent race.</summary>
    public byte[] RowVersion { get; private set; } = [];

    private WorkItem() { }

    public static WorkItem Create(
        Guid organizationId,
        Guid workflowInstanceId,
        Guid activityInstanceId,
        Guid assignmentGroupId,
        DateTime createdAt,
        DateTime? dueAt = null)
    {
        return new WorkItem
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            WorkflowInstanceId = workflowInstanceId,
            ActivityInstanceId = activityInstanceId,
            AssignmentGroupId = assignmentGroupId,
            Status = WorkItemStatus.Pending,
            DueAt = dueAt,
            CreatedAt = createdAt,
            FormReceiptAttempt = Guid.NewGuid(),
        };
    }

    public void Claim(Guid userId, DateTime claimedAt)
    {
        if (Status != WorkItemStatus.Pending)
            throw new InvalidOperationException("Only Pending work items can be claimed.");

        ClaimedByUserId = userId;
        ClaimedAt = claimedAt;
        Status = WorkItemStatus.Claimed;
        SetUpdated(claimedAt);
    }

    public void Release(DateTime releasedAt)
    {
        if (Status != WorkItemStatus.Claimed)
            throw new InvalidOperationException("Only Claimed work items can be released.");

        ClaimedByUserId = null;
        ClaimedAt = null;
        FormReceiptAttempt = Guid.NewGuid();
        Status = WorkItemStatus.Pending;
        SetUpdated(releasedAt);
    }

    public void Complete(Guid userId, string actionTaken, DateTime completedAt, string? comment = null)
    {
        if (Status != WorkItemStatus.Claimed)
            throw new InvalidOperationException("Only Claimed work items can be completed.");

        CompletedByUserId = userId;
        CompletedAt = completedAt;
        ActionTaken = actionTaken;
        CommentText = comment;
        Status = WorkItemStatus.Completed;
        SetUpdated(completedAt);
    }

    public void Cancel(DateTime cancelledAt)
    {
        Status = WorkItemStatus.Cancelled;
        SetUpdated(cancelledAt);
    }

    public void Reassign(Guid newAssignmentGroupId, DateTime reassignedAt)
    {
        if (Status is WorkItemStatus.Completed or WorkItemStatus.Cancelled)
            throw new InvalidOperationException("Cannot reassign a completed or cancelled work item.");

        AssignmentGroupId = newAssignmentGroupId;
        FormReceiptAttempt = Guid.NewGuid();
        ClaimedByUserId = null;
        ClaimedAt = null;
        if (Status == WorkItemStatus.Claimed)
            Status = WorkItemStatus.Pending;
        SetUpdated(reassignedAt);
    }

    public void SetDueAt(DateTime? dueAt, DateTime updatedAt)
    {
        DueAt = dueAt;
        SetUpdated(updatedAt);
    }

    /// <summary>Transfers claim ownership to another user without releasing to the group inbox.</summary>
    public void Delegate(Guid toUserId, DateTime delegatedAt)
    {
        if (Status != WorkItemStatus.Claimed)
            throw new InvalidOperationException("Only Claimed work items can be delegated.");

        if (ClaimedByUserId != toUserId) FormReceiptAttempt = Guid.NewGuid();
        ClaimedByUserId = toUserId;
        ClaimedAt = delegatedAt;
        SetUpdated(delegatedAt);
    }
}
