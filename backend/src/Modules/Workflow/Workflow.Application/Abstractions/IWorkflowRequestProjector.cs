namespace Workflow.Application.Abstractions;

using Workflow.Domain.Entities;

public interface IWorkflowRequestProjector
{
    Task EnsureCreatedAsync(WorkflowInstance instance, WorkflowBinding binding, DateTime now, CancellationToken cancellationToken = default);

    Task SyncCurrentTaskAsync(
        WorkflowInstance instance,
        WorkItem? workItem,
        string? activityNameEn,
        string? activityNameAr,
        DateTime now,
        CancellationToken cancellationToken = default);

    Task SyncStatusAsync(WorkflowInstance instance, DateTime now, CancellationToken cancellationToken = default);

    Task SyncClaimAsync(Guid workflowInstanceId, Guid organizationId, Guid? claimedByUserId, DateTime now, CancellationToken cancellationToken = default);

    Task SyncAssignmentAsync(Guid workflowInstanceId, Guid organizationId, Guid assignmentGroupId,
        Guid? claimedByUserId, DateTime now, CancellationToken cancellationToken = default);

    Task BackfillMissingForOrgAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
