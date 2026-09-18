namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using NWFM.Shared.MultiTenancy;
using Workflow.Domain.Enums;

/// <summary>
/// Parallel gateway execution token. Unique per (WorkflowInstanceId, BranchKey).
/// </summary>
public sealed class WorkflowExecutionToken : Entity, ITenantAware
{
    public Guid OrganizationId { get; private set; }
    public Guid WorkflowInstanceId { get; private set; }
    public string ParallelGatewayNodeKey { get; private set; } = string.Empty;
    public string BranchKey { get; private set; } = string.Empty;
    public ExecutionTokenStatus Status { get; private set; }
    public string? JoinNodeKey { get; private set; }
    public Guid? ParentTokenId { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private WorkflowExecutionToken() { }

    public static WorkflowExecutionToken Create(
        Guid organizationId,
        Guid workflowInstanceId,
        string parallelGatewayNodeKey,
        string branchKey,
        DateTime createdAt,
        string? joinNodeKey = null,
        Guid? parentTokenId = null)
    {
        return new WorkflowExecutionToken
        {
            Id                      = Guid.NewGuid(),
            OrganizationId          = organizationId,
            WorkflowInstanceId      = workflowInstanceId,
            ParallelGatewayNodeKey  = parallelGatewayNodeKey,
            BranchKey               = branchKey,
            Status                  = ExecutionTokenStatus.Active,
            JoinNodeKey             = joinNodeKey,
            ParentTokenId           = parentTokenId,
            CreatedAt               = createdAt,
        };
    }

    public void Complete(DateTime completedAt)
    {
        Status      = ExecutionTokenStatus.Completed;
        CompletedAt = completedAt;
        SetUpdated(completedAt);
    }

    public void Cancel(DateTime cancelledAt)
    {
        Status      = ExecutionTokenStatus.Cancelled;
        CancelledAt = cancelledAt;
        SetUpdated(cancelledAt);
    }

    public void Fail(DateTime failedAt)
    {
        Status = ExecutionTokenStatus.Failed;
        SetUpdated(failedAt);
    }
}
