namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NWFM.Shared.Integration.Workflow;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Domain.Repositories;
using global::Workflow.Infrastructure.Services;

public sealed class WorkflowOutcomeDispatcherTests
{
    [Fact]
    public async Task Dispatch_ShadowOutcome_PersistsEvidenceWithoutCallingBusinessHandler()
    {
        var organizationId = Guid.NewGuid();
        var bindingId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var message = new WorkflowOutcomeMessage(
            organizationId,
            bindingId,
            instanceId,
            "Consent",
            "ConsentRequest",
            "demo-entity",
            "APPROVE",
            "shadow-test",
            new Dictionary<string, object?> { ["IsShadow"] = true },
            DateTime.UtcNow,
            "shadow-message");
        var outboxRow = WorkflowIntegrationOutbox.Create(
            organizationId, message.MessageId, bindingId, instanceId,
            message.ModuleKey, message.BusinessEntityType, message.BusinessEntityId,
            message.OutcomeKey, "{\"IsShadow\":true}", message.OccurredAt, message.CorrelationId);

        var publisher = new Mock<IWorkflowOutcomePublisher>();
        publisher.Setup(x => x.PublishAsync(message, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var outbox = new Mock<IWorkflowIntegrationOutboxRepository>();
        outbox.Setup(x => x.GetByMessageIdAsync(message.MessageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(outboxRow);
        outbox.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var handler = new Mock<IWorkflowOutcomeHandler>();
        handler.SetupGet(x => x.ModuleKey).Returns("Consent");
        var dispatcher = new WorkflowOutcomeDispatcher(
            publisher.Object, outbox.Object, [handler.Object],
            NullLogger<WorkflowOutcomeDispatcher>.Instance);

        var result = await dispatcher.DispatchAsync(message, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        outboxRow.Status.Should().Be(WorkflowOutboxStatus.Published);
        handler.Verify(x => x.HandleAsync(It.IsAny<WorkflowOutcomeMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
        outbox.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
