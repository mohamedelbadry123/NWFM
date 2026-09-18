namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using Moq;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.Commands.ClaimWorkItem;
using global::Workflow.Application.Constants;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Domain.Repositories;
using NWFM.Shared.Results;

public sealed class ClaimWorkItemCommandHandlerTests
{
    private readonly Mock<IWorkflowFeatureGate> _gate = new();
    private readonly Mock<IWorkItemRepository> _workItemRepo = new();
    private readonly Mock<IWorkflowAssignmentGroupRepository> _groupRepo = new();
    private readonly Mock<IWorkflowParticipantRepository> _participantRepo = new();
    private readonly Mock<IWorkflowEventAppender> _events = new();
    private readonly Mock<IWorkItemDtoAssembler> _assembler = new();
    private readonly Mock<IWorkflowRequestProjector> _projector = new();
    private readonly ClaimWorkItemCommandHandler _handler;

    public ClaimWorkItemCommandHandlerTests()
    {
        _handler = new ClaimWorkItemCommandHandler(
            _gate.Object,
            _workItemRepo.Object,
            _groupRepo.Object,
            _participantRepo.Object,
            _events.Object,
            _assembler.Object,
            _projector.Object);
    }

    private static ClaimWorkItemCommand MakeCommand(Guid workItemId, Guid userId, Guid orgId) =>
        new(workItemId, userId, orgId);

    [Fact]
    public async Task Handle_WhenWorkItemNotFound_ReturnsNotFound()
    {
        var orgId = Guid.NewGuid();
        _gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
        _workItemRepo
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkItem?)null);

        var result = await _handler.Handle(MakeCommand(Guid.NewGuid(), Guid.NewGuid(), orgId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WorkflowErrors.WorkItem.NotFound);
    }

    [Fact]
    public async Task Handle_WhenWorkItemNotPending_ReturnsNotPending()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var item = WorkItem.Create(
            orgId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        item.Claim(userId, DateTime.UtcNow);

        _gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
        _workItemRepo
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        var result = await _handler.Handle(MakeCommand(item.Id, userId, orgId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WorkflowErrors.WorkItem.NotPending);
    }

    [Fact]
    public async Task Handle_WhenUserNotGroupMember_ReturnsNotGroupMember()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var item = WorkItem.Create(
            orgId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);

        _gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
        _workItemRepo
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        _participantRepo
            .Setup(r => r.ExistsActiveForUserAsync(userId, orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _groupRepo
            .Setup(r => r.IsUserMemberOfGroupAsync(
                item.AssignmentGroupId, userId, orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(MakeCommand(item.Id, userId, orgId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WorkflowErrors.WorkItem.NotGroupMember);
    }

    [Fact]
    public async Task Handle_WhenMembershipExpired_ReturnsNotGroupMember()
    {
        // Membership validity is enforced inside IsUserMemberOfGroupAsync (ValidTo / IsActive).
        // When expired, the repository returns false → NotGroupMember.
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var item = WorkItem.Create(
            orgId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);

        _gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
        _workItemRepo
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        _participantRepo
            .Setup(r => r.ExistsActiveForUserAsync(userId, orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _groupRepo
            .Setup(r => r.IsUserMemberOfGroupAsync(
                item.AssignmentGroupId, userId, orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(MakeCommand(item.Id, userId, orgId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WorkflowErrors.WorkItem.NotGroupMember);
    }

    [Fact]
    public async Task Handle_WhenParticipantInactive_ReturnsParticipantNotFound()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var item = WorkItem.Create(
            orgId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);

        _gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
        _workItemRepo
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        _participantRepo
            .Setup(r => r.ExistsActiveForUserAsync(userId, orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(MakeCommand(item.Id, userId, orgId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WorkflowErrors.Participant.NotFound);
    }
}
