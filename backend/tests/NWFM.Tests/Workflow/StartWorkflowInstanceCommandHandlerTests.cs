namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using Moq;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.Commands.StartWorkflowInstance;
using global::Workflow.Application.Constants;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Domain.Repositories;
using NWFM.Shared.Results;

public sealed class StartWorkflowInstanceCommandHandlerTests
{
    private readonly Mock<IWorkflowFeatureGate> _gate = new();
    private readonly Mock<IWorkflowBindingRepository> _bindingRepo = new();
    private readonly Mock<IWorkflowInstanceRepository> _instanceRepo = new();
    private readonly Mock<IWorkflowRuntimeEngine> _engine = new();
    private readonly StartWorkflowInstanceCommandHandler _handler;

    public StartWorkflowInstanceCommandHandlerTests()
    {
        _handler = new StartWorkflowInstanceCommandHandler(
            _gate.Object, _bindingRepo.Object, _instanceRepo.Object, _engine.Object);
    }

    private static StartWorkflowInstanceCommand MakeCommand(Guid orgId, Guid bindingId, string idemKey = "key-1") =>
        new(orgId, bindingId, "entity-1", idemKey);

    [Fact]
    public async Task Handle_WhenFeatureGateDisabled_ReturnsModuleDisabled()
    {
        _gate.Setup(g => g.EnsureEnabled())
            .Returns(Result.Failure(WorkflowErrors.ModuleDisabled));

        var result = await _handler.Handle(MakeCommand(Guid.NewGuid(), Guid.NewGuid()), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WorkflowErrors.ModuleDisabled);
    }

    [Fact]
    public async Task Handle_WhenBindingNotFound_ReturnsBindingNotFound()
    {
        var orgId = Guid.NewGuid();
        _gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
        _bindingRepo
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowBinding?)null);

        var result = await _handler.Handle(MakeCommand(orgId, Guid.NewGuid()), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WorkflowErrors.Binding.NotFound);
    }

    [Fact]
    public async Task Handle_WhenDuplicateIdempotencyKey_ReturnsAlreadyStarted()
    {
        var orgId = Guid.NewGuid();
        var bindingId = Guid.NewGuid();
        var binding = WorkflowBinding.Create(
            Guid.NewGuid(), orgId, "Consent", "ConsentRequest", "Created", DateTime.UtcNow);

        _gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
        _bindingRepo
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(binding);
        _instanceRepo
            .Setup(r => r.ExistsByIdempotencyKeyAsync(orgId, "key-dup", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(MakeCommand(orgId, bindingId, "key-dup"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WorkflowErrors.Instance.AlreadyStarted);
    }
}
