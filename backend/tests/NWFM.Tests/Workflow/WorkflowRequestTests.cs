namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;

public sealed class WorkflowRequestTests
{
    [Fact]
    public void Create_SetsRequestNumberAndRunningStatus()
    {
        var orgId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var request = WorkflowRequest.Create(
            orgId, "WF-2026-000001", Guid.NewGuid(), instanceId,
            "ConsentRequest", instanceId.ToString(), "Consent", "Consent Request",
            now, WorkflowInstanceStatus.Running, "ConsentRequest.Created", now);

        request.RequestNumber.Should().Be("WF-2026-000001");
        request.Status.Should().Be(WorkflowInstanceStatus.Running);
        request.OriginalAssignedGroupId.Should().BeNull();
        request.OrganizationId.Should().Be(orgId);
    }

    [Fact]
    public void SyncCurrentTask_SetsOriginalGroupOnlyOnce()
    {
        var request = WorkflowRequest.Create(
            Guid.NewGuid(), "WF-2026-000002", Guid.NewGuid(), Guid.NewGuid(),
            "ConsentRequest", "e1", "Consent", "Consent Request",
            DateTime.UtcNow, WorkflowInstanceStatus.Running, "ConsentRequest.Created", DateTime.UtcNow);

        var firstGroup = Guid.NewGuid();
        var secondGroup = Guid.NewGuid();
        var now = DateTime.UtcNow;

        request.SyncCurrentTask(Guid.NewGuid(), "Review", null, firstGroup, 60, now.AddHours(1), null, now);
        request.OriginalAssignedGroupId.Should().Be(firstGroup);
        request.CurrentAssignedGroupId.Should().Be(firstGroup);

        request.SyncCurrentTask(Guid.NewGuid(), "Approve", null, secondGroup, 30, now.AddHours(2), Guid.NewGuid(), now.AddMinutes(1));
        request.OriginalAssignedGroupId.Should().Be(firstGroup);
        request.CurrentAssignedGroupId.Should().Be(secondGroup);
        request.CurrentActivityNameEn.Should().Be("Approve");
    }

    [Fact]
    public void SyncStatus_Completed_ClearsActiveSlaClock()
    {
        var request = WorkflowRequest.Create(
            Guid.NewGuid(), "WF-2026-000003", Guid.NewGuid(), Guid.NewGuid(),
            "ConsentRequest", "e1", "Consent", "Consent Request",
            DateTime.UtcNow, WorkflowInstanceStatus.Running, "ConsentRequest.Created", DateTime.UtcNow);

        var now = DateTime.UtcNow;
        request.SyncCurrentTask(Guid.NewGuid(), "Review", null, Guid.NewGuid(), 60, now.AddHours(1), Guid.NewGuid(), now);
        request.SyncStatus(WorkflowInstanceStatus.Completed, now, now);

        request.Status.Should().Be(WorkflowInstanceStatus.Completed);
        request.CurrentTaskDueAtUtc.Should().BeNull();
        request.CurrentClaimedByUserId.Should().BeNull();
        request.CompletedAtUtc.Should().Be(now);
    }

    [Fact]
    public void SyncAssignment_ChangesCurrentGroupAndClaimantWithoutLosingOriginalGroup()
    {
        var request = WorkflowRequest.Create(
            Guid.NewGuid(), "WF-2026-000004", Guid.NewGuid(), Guid.NewGuid(),
            "ConsentRequest", "e1", "Consent", "Consent Request",
            DateTime.UtcNow, WorkflowInstanceStatus.Running, "ConsentRequest.Created", DateTime.UtcNow);
        var originalGroup = Guid.NewGuid();
        var reassignedGroup = Guid.NewGuid();
        var delegateId = Guid.NewGuid();

        request.SyncAssignment(originalGroup, null, DateTime.UtcNow);
        request.SyncAssignment(reassignedGroup, delegateId, DateTime.UtcNow.AddMinutes(1));

        request.OriginalAssignedGroupId.Should().Be(originalGroup);
        request.CurrentAssignedGroupId.Should().Be(reassignedGroup);
        request.CurrentClaimedByUserId.Should().Be(delegateId);
    }

    [Fact]
    public void WorkflowRequest_ImplementsITenantAware()
    {
        typeof(WorkflowRequest).Should().Implement<NWFM.Shared.MultiTenancy.ITenantAware>();
    }
}
