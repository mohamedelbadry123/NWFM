namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;

public sealed class WorkflowInstanceTests
{
    private static WorkflowInstance MakeRunning() =>
        WorkflowInstance.Start(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "idem-1", "entity-1", "Start", DateTime.UtcNow, "corr-1");

    [Fact]
    public void Start_SetsStatusToRunning()
    {
        var inst = MakeRunning();
        inst.Status.Should().Be(WorkflowInstanceStatus.Running);
        inst.IdempotencyKey.Should().Be("idem-1");
        inst.BusinessEntityId.Should().Be("entity-1");
        inst.CorrelationId.Should().Be("corr-1");
    }

    [Fact]
    public void Complete_SetsStatusAndTimestamp()
    {
        var inst = MakeRunning();
        var now = DateTime.UtcNow;
        inst.Complete(now);
        inst.Status.Should().Be(WorkflowInstanceStatus.Completed);
        inst.CompletedAt.Should().Be(now);
    }

    [Fact]
    public void Cancel_SetsStatusAndTimestamp()
    {
        var inst = MakeRunning();
        var now = DateTime.UtcNow;
        inst.Cancel(now);
        inst.Status.Should().Be(WorkflowInstanceStatus.Cancelled);
        inst.CancelledAt.Should().Be(now);
    }

    [Fact]
    public void Suspend_SetsStatusToSuspended()
    {
        var inst = MakeRunning();
        var now = DateTime.UtcNow;
        inst.Suspend(now);
        inst.Status.Should().Be(WorkflowInstanceStatus.Suspended);
        inst.SuspendedAt.Should().Be(now);
    }

    [Fact]
    public void Resume_ClearsSuspendedAtAndRestoresRunning()
    {
        var inst = MakeRunning();
        var suspended = DateTime.UtcNow;
        inst.Suspend(suspended);
        inst.Resume(DateTime.UtcNow.AddSeconds(1));
        inst.Status.Should().Be(WorkflowInstanceStatus.Running);
        inst.SuspendedAt.Should().BeNull();
    }

    [Fact]
    public void Fail_SetsStatusAndReason()
    {
        var inst = MakeRunning();
        inst.Fail("engine error", DateTime.UtcNow);
        inst.Status.Should().Be(WorkflowInstanceStatus.Failed);
        inst.FailureReason.Should().Be("engine error");
    }

    [Fact]
    public void AdvanceTo_UpdatesCurrentActivityNodeKey()
    {
        var inst = MakeRunning();
        inst.AdvanceTo("ReviewTask", DateTime.UtcNow);
        inst.CurrentActivityNodeKey.Should().Be("ReviewTask");
    }

    [Fact]
    public void Start_WithNullStartedByUserId_IsSystemTriggered()
    {
        var inst = WorkflowInstance.Start(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "idem-sys", "entity-sys", "Start", DateTime.UtcNow);
        inst.StartedByUserId.Should().BeNull();
    }
}
