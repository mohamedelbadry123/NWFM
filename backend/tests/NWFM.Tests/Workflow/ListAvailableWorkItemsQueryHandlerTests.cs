namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using Moq;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.DTOs;
using global::Workflow.Application.Queries.ListAvailableWorkItems;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Repositories;
using NWFM.Shared.Results;

public sealed class ListAvailableWorkItemsQueryHandlerTests
{
    private readonly Mock<IWorkflowFeatureGate> _gate = new();
    private readonly Mock<IWorkItemRepository> _workItems = new();
    private readonly Mock<IWorkflowAssignmentGroupRepository> _groups = new();
    private readonly Mock<IWorkflowParticipantRepository> _participants = new();
    private readonly Mock<IWorkItemDtoAssembler> _assembler = new();

    public ListAvailableWorkItemsQueryHandlerTests()
    {
        _gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
        _assembler
            .Setup(a => a.ToDtoListAsync(It.IsAny<IReadOnlyList<WorkItem>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WorkItem> items, CancellationToken _) =>
                items.Select(ToDto).ToList());
    }

    [Fact]
    public async Task Handle_WhenUserIsGroupMember_ReturnsThoseGroupsOnly()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var mine = WorkItem.Create(orgId, Guid.NewGuid(), Guid.NewGuid(), groupId, DateTime.UtcNow);

        _groups.Setup(g => g.GetGroupIdsForUserAsync(userId, orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { groupId });
        _workItems.Setup(r => r.GetPendingForGroupIdsAsync(orgId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { mine });

        var result = await Handler().Handle(new ListAvailableWorkItemsQuery(userId, orgId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Id == mine.Id);
        _workItems.Verify(r => r.GetPendingForOrganizationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenNotAParticipant_ReturnsAllOrgPending()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var pending = WorkItem.Create(orgId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);

        _groups.Setup(g => g.GetGroupIdsForUserAsync(userId, orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());
        _participants.Setup(p => p.ExistsActiveForUserAsync(userId, orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _workItems.Setup(r => r.GetPendingForOrganizationAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { pending });

        var result = await Handler().Handle(new ListAvailableWorkItemsQuery(userId, orgId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Id == pending.Id);
    }

    [Fact]
    public async Task Handle_WhenParticipantHasNoGroup_ReturnsEmpty()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        _groups.Setup(g => g.GetGroupIdsForUserAsync(userId, orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());
        _participants.Setup(p => p.ExistsActiveForUserAsync(userId, orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await Handler().Handle(new ListAvailableWorkItemsQuery(userId, orgId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        _workItems.Verify(r => r.GetPendingForOrganizationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private ListAvailableWorkItemsQueryHandler Handler() => new(
        _gate.Object, _workItems.Object, _groups.Object, _participants.Object, _assembler.Object);

    private static WorkItemDto ToDto(WorkItem item) => new(
        item.Id, item.WorkflowInstanceId, item.ActivityInstanceId, item.OrganizationId,
        item.AssignmentGroupId, null, item.ClaimedByUserId, item.ClaimedAt,
        item.CompletedByUserId, item.CompletedAt, item.DueAt, item.Status,
        item.ActionTaken, item.CommentText, item.CreatedAt, item.UpdatedAt);
}
