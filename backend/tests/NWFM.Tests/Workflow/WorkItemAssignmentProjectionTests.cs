namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using Moq;
using NWFM.Shared.Results;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.Commands.DelegateWorkItem;
using global::Workflow.Application.Commands.ReassignWorkItem;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Domain.Repositories;

public sealed class WorkItemAssignmentProjectionTests
{
    [Fact]
    public async Task Delegate_UpdatesWorkflowRequestToTheNewClaimant()
    {
        var organizationId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var delegateId = Guid.NewGuid();
        var item = WorkItem.Create(
            organizationId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        item.Claim(actorId, DateTime.UtcNow);

        var gate = EnabledGate();
        var workItems = WorkItems(item);
        var groups = new Mock<IWorkflowAssignmentGroupRepository>();
        groups.Setup(x => x.IsUserMemberOfGroupAsync(
                item.AssignmentGroupId, delegateId, organizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var participants = new Mock<IWorkflowParticipantRepository>();
        participants.Setup(x => x.ExistsActiveForUserAsync(
                delegateId, organizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var events = EventAppender();
        var projector = new Mock<IWorkflowRequestProjector>();
        projector.Setup(x => x.SyncAssignmentAsync(
                item.WorkflowInstanceId, organizationId, item.AssignmentGroupId, delegateId,
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var handler = new DelegateWorkItemCommandHandler(
            gate.Object, workItems.Object, groups.Object, participants.Object, events.Object, projector.Object);

        var result = await handler.Handle(new DelegateWorkItemCommand(
            item.Id, organizationId, actorId, delegateId, "Coverage handoff"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        item.ClaimedByUserId.Should().Be(delegateId);
        projector.Verify(x => x.SyncAssignmentAsync(
            item.WorkflowInstanceId, organizationId, item.AssignmentGroupId, delegateId,
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reassign_UpdatesWorkflowRequestToTheNewGroupAndClearsClaimant()
    {
        var organizationId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var newGroup = WorkflowAssignmentGroup.Create(
            organizationId, "LEGAL", "Legal reviewers", AssignmentStrategy.Manual, DateTime.UtcNow);
        var item = WorkItem.Create(
            organizationId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        item.Claim(actorId, DateTime.UtcNow);

        var gate = EnabledGate();
        var workItems = WorkItems(item);
        var groups = new Mock<IWorkflowAssignmentGroupRepository>();
        groups.Setup(x => x.GetByIdAsync(newGroup.Id, organizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newGroup);
        var candidateFactory = new Mock<IWorkflowCandidateFactory>();
        candidateFactory.Setup(x => x.CreateCandidatesAsync(
                organizationId, item.Id, newGroup.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WorkItemCandidate>());
        var candidates = new Mock<IWorkItemCandidateRepository>();
        candidates.Setup(x => x.AddRangeAsync(
                It.IsAny<IEnumerable<WorkItemCandidate>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var events = EventAppender();
        var projector = new Mock<IWorkflowRequestProjector>();
        projector.Setup(x => x.SyncAssignmentAsync(
                item.WorkflowInstanceId, organizationId, newGroup.Id, null,
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var handler = new ReassignWorkItemCommandHandler(
            gate.Object, workItems.Object, groups.Object, candidateFactory.Object,
            candidates.Object, events.Object, projector.Object);

        var result = await handler.Handle(new ReassignWorkItemCommand(
            item.Id, organizationId, actorId, newGroup.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        item.AssignmentGroupId.Should().Be(newGroup.Id);
        item.Status.Should().Be(WorkItemStatus.Pending);
        item.ClaimedByUserId.Should().BeNull();
        projector.Verify(x => x.SyncAssignmentAsync(
            item.WorkflowInstanceId, organizationId, newGroup.Id, null,
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Mock<IWorkflowFeatureGate> EnabledGate()
    {
        var gate = new Mock<IWorkflowFeatureGate>();
        gate.Setup(x => x.EnsureEnabled()).Returns(Result.Success());
        return gate;
    }

    private static Mock<IWorkItemRepository> WorkItems(WorkItem item)
    {
        var repository = new Mock<IWorkItemRepository>();
        repository.Setup(x => x.GetByIdAsync(item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        repository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return repository;
    }

    private static Mock<IWorkflowEventAppender> EventAppender()
    {
        var events = new Mock<IWorkflowEventAppender>();
        events.Setup(x => x.AppendAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<WorkflowEventType>(), It.IsAny<DateTime>(),
                It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return events;
    }
}
