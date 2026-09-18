namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using Moq;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.Commands.SimulateWorkflowBinding;
using global::Workflow.Application.Constants;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Repositories;
using NWFM.Shared.Results;

public sealed class SimulateWorkflowBindingCommandHandlerTests
{
    private readonly Mock<IWorkflowFeatureGate> _gate = new();
    private readonly Mock<IWorkflowBindingRepository> _bindingRepo = new();
    private readonly Mock<IWorkflowVersionResolver> _versionResolver = new();
    private readonly Mock<IWorkflowTransitionEvaluator> _evaluator = new();
    private readonly SimulateWorkflowBindingCommandHandler _handler;

    public SimulateWorkflowBindingCommandHandlerTests()
    {
        _handler = new SimulateWorkflowBindingCommandHandler(
            _gate.Object, _bindingRepo.Object, _versionResolver.Object, _evaluator.Object);
    }

    [Fact]
    public async Task Handle_WhenTenantFilterHidesBinding_UsesSuperAdminLookup()
    {
        var orgId = Guid.NewGuid();
        var binding = WorkflowBinding.Create(
            Guid.NewGuid(), orgId, "Consent", "ConsentRequest", "Created", DateTime.UtcNow);
        var version = WorkflowVersion.CreateDraft(binding.WorkflowDefinitionId, 1, Guid.NewGuid(), DateTime.UtcNow);

        _gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
        _bindingRepo
            .Setup(r => r.GetByIdAsync(binding.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowBinding?)null);
        _bindingRepo
            .Setup(r => r.GetByIdForSuperAdminAsync(binding.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(binding);
        _versionResolver
            .Setup(r => r.ResolveAsync(binding, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(version));

        var result = await _handler.Handle(
            new SimulateWorkflowBindingCommand(binding.Id, orgId, """{"entityId":"00000000-0000-0000-0000-000000000001"}"""),
            default);

        result.IsSuccess.Should().BeTrue();
        _bindingRepo.Verify(r => r.GetByIdForSuperAdminAsync(binding.Id, It.IsAny<CancellationToken>()), Times.Once);
        _bindingRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenOrganizationDoesNotMatch_ReturnsBindingNotFound()
    {
        var bindingOrg = Guid.NewGuid();
        var otherOrg = Guid.NewGuid();
        var binding = WorkflowBinding.Create(
            Guid.NewGuid(), bindingOrg, "Consent", "ConsentRequest", "Created", DateTime.UtcNow);

        _gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
        _bindingRepo
            .Setup(r => r.GetByIdForSuperAdminAsync(binding.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(binding);

        var result = await _handler.Handle(
            new SimulateWorkflowBindingCommand(binding.Id, otherOrg, null),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WorkflowErrors.Binding.NotFound);
    }
}
