namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.Commands.ClaimWorkItem;
using global::Workflow.Application.Commands.CompleteWorkItem;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Domain.Repositories;
using Moq;
using NWFM.Shared.Results;

public sealed class WorkflowRoleCommandGuardsTests
{
    [Fact]
    public async Task Claim_RejectsAUserWhoseRoleWasRevoked_BeforeAnyStateChange()
    {
        var item = WorkItem.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, DateTime.UtcNow);
        var user = Guid.NewGuid();
        var gate = new Mock<IWorkflowFeatureGate>(); gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
        var items = new Mock<IWorkItemRepository>(); items.Setup(r => r.GetByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        var participants = new Mock<IWorkflowParticipantRepository>(); participants.Setup(p => p.ExistsActiveForUserAsync(user, item.OrganizationId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var access = new Mock<IWorkflowRoleAssignments>(); access.Setup(a => a.CanAccessAsync(item, user, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var handler = new ClaimWorkItemCommandHandler(gate.Object, items.Object, Mock.Of<IWorkflowAssignmentGroupRepository>(), participants.Object,
            Mock.Of<IWorkflowEventAppender>(), Mock.Of<IWorkItemDtoAssembler>(), Mock.Of<IWorkflowRequestProjector>(), access.Object);
        var result = await handler.Handle(new(item.Id, user, item.OrganizationId), default);
        result.IsFailure.Should().BeTrue(); result.Error.Code.Should().Be("Workflow.WorkItem.NotAssignedRole");
        item.Status.Should().Be(WorkItemStatus.Pending);
        items.Verify(r => r.TryClaimAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Complete_RejectsAFormerRoleMember_EvenForALegacyVersionWithoutWorkspaceSettings()
    {
        var user = Guid.NewGuid();
        var item = WorkItem.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, DateTime.UtcNow); item.Claim(user, DateTime.UtcNow);
        var gate = new Mock<IWorkflowFeatureGate>(); gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
        var items = new Mock<IWorkItemRepository>(); items.Setup(r => r.GetByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        var access = new Mock<IWorkflowRoleAssignments>(); access.Setup(a => a.CanAccessAsync(item, user, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var handler = new CompleteWorkItemCommandHandler(gate.Object, items.Object, Mock.Of<IWorkflowRuntimeEngine>(), Mock.Of<IWorkflowEventAppender>(),
            Mock.Of<IWorkItemDtoAssembler>(), Mock.Of<IWorkflowInstanceRepository>(), Mock.Of<IWorkflowVersionRepository>(), Mock.Of<IActivityInstanceRepository>(),
            Mock.Of<IWorkflowAssignmentGroupRepository>(), Mock.Of<IWorkflowDepartmentRepository>(), Mock.Of<IWorkflowCandidateFactory>(), Mock.Of<IWorkItemCandidateRepository>(), roles: access.Object);
        var result = await handler.Handle(new(item.Id, user, item.OrganizationId, "APPROVE"), default);
        result.IsFailure.Should().BeTrue(); result.Error.Code.Should().Be("Workflow.Assignment.Membership");
        item.Status.Should().Be(WorkItemStatus.Claimed);
        items.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
