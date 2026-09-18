namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using Moq;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.Constants;
using global::Workflow.Application.Queries.GetWorkflowLiveGraph;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Repositories;
using NWFM.Shared.Results;

public sealed class GetWorkflowLiveGraphQueryHandlerTests
{
    private readonly Mock<IWorkflowFeatureGate> _gate = new();
    private readonly Mock<IWorkflowInstanceRepository> _instances = new();
    private readonly Mock<IWorkflowVersionRepository> _versions = new();
    private readonly Mock<IActivityInstanceRepository> _activities = new();
    private readonly GetWorkflowLiveGraphQueryHandler _handler;

    public GetWorkflowLiveGraphQueryHandlerTests()
    {
        _handler = new GetWorkflowLiveGraphQueryHandler(
            _gate.Object, _instances.Object, _versions.Object, _activities.Object);
    }

    [Fact]
    public async Task Handle_WhenSuperAdmin_UsesCrossTenantLookups()
    {
        var orgId = Guid.NewGuid();
        var version = WorkflowVersion.CreateDraft(Guid.NewGuid(), 1, Guid.NewGuid(), DateTime.UtcNow);
        var instance = WorkflowInstance.Start(
            orgId, Guid.NewGuid(), version.Id, "key", "entity-1", "start", DateTime.UtcNow);

        _gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
        _instances
            .Setup(r => r.GetByIdForSuperAdminAsync(instance.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _versions
            .Setup(r => r.GetByIdWithProjectionAsync(version.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(version);
        _activities
            .Setup(r => r.GetByInstanceIdForSuperAdminAsync(instance.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ActivityInstance>());

        var result = await _handler.Handle(
            new GetWorkflowLiveGraphQuery(instance.Id, Guid.Empty, SuperAdmin: true), default);

        result.IsSuccess.Should().BeTrue();
        _instances.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _activities.Verify(r => r.GetByInstanceIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _activities.Verify(r => r.GetByInstanceIdForSuperAdminAsync(instance.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenOrgMismatch_ReturnsNotFound()
    {
        var instance = WorkflowInstance.Start(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "key", "entity-1", "start", DateTime.UtcNow);

        _gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
        _instances
            .Setup(r => r.GetByIdAsync(instance.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        var result = await _handler.Handle(
            new GetWorkflowLiveGraphQuery(instance.Id, Guid.NewGuid(), SuperAdmin: false), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WorkflowErrors.Instance.NotFound);
    }
}
