namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NWFM.Shared.Results;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.Commands.ReplayInboxMessage;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Domain.Repositories;
using global::Workflow.Infrastructure.Background;

public sealed class WorkflowInboxRecoveryTests
{
    [Fact]
    public async Task Replay_WhenExisting_InboxWithExistingIdempotency_MarksExistingInstanceProcessedWithoutRestart()
    {
        var now = new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);
        var organizationId = Guid.NewGuid();
        var bindingId = Guid.NewGuid();
        var idempotencyKey = "demo:workflow/runtime/namaa/integration-retry";
        var instance = WorkflowInstance.Start(
            organizationId, bindingId, Guid.NewGuid(), idempotencyKey, "demo-entity", "REVIEW_1", now);
        instance.Fail("Controlled synthetic failure.", now.AddMinutes(1));

        var message = WorkflowIntegrationInbox.Create(
            organizationId,
            "demo-retry-inbox:test",
            idempotencyKey,
            "DemoOperations",
            "WorkflowRecovery",
            "demo-entity",
            "WorkflowRecovery.RetryRequested",
            "{\"syntheticDemo\":true}",
            now,
            correlationId: "recovery-test",
            bindingId: bindingId);
        message.MarkFailed("Synthetic recoverable failure.", now.AddMinutes(2));
        message.MarkDeadLetter(now.AddMinutes(3));

        var gate = new Mock<IWorkflowFeatureGate>();
        gate.Setup(x => x.EnsureEnabled()).Returns(Result.Success());
        var inbox = new Mock<IWorkflowIntegrationInboxRepository>();
        inbox.Setup(x => x.GetByIdAsync(message.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);
        inbox.Setup(x => x.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => message.Status == WorkflowInboxStatus.Pending
                ? new[] { message }
                : Array.Empty<WorkflowIntegrationInbox>());
        inbox.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var replay = new ReplayInboxMessageCommandHandler(gate.Object, inbox.Object);
        var replayResult = await replay.Handle(
            new ReplayInboxMessageCommand(message.Id, organizationId), CancellationToken.None);
        replayResult.IsSuccess.Should().BeTrue();
        message.Status.Should().Be(WorkflowInboxStatus.Pending);

        var instances = new Mock<IWorkflowInstanceRepository>();
        instances.Setup(x => x.ExistsByIdempotencyKeyAsync(
                organizationId, idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        instances.Setup(x => x.GetByIdempotencyKeyAsync(
                organizationId, idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        var engine = new Mock<IWorkflowRuntimeEngine>();

        await using var services = new ServiceCollection()
            .AddSingleton(inbox.Object)
            .AddSingleton(instances.Object)
            .AddSingleton(engine.Object)
            .BuildServiceProvider();
        var hosted = new WorkflowInboxHostedService(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WorkflowInboxHostedService>.Instance);

        await hosted.ProcessPendingBatchAsync(CancellationToken.None);
        await hosted.ProcessPendingBatchAsync(CancellationToken.None);

        message.Status.Should().Be(WorkflowInboxStatus.Processed);
        message.WorkflowInstanceId.Should().Be(instance.Id);
        message.AttemptCount.Should().Be(0, "idempotency recovery does not execute the workflow again");
        engine.Invocations.Should().BeEmpty();
        inbox.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
