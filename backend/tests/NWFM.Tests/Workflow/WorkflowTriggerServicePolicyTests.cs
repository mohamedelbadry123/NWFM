namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Infrastructure.Services;

/// <summary>
/// Tests WorkflowTriggerService.DecideTriggerAction for all four WorkflowExecutionPolicy values
/// against every meaningful instance state (null, Running, Suspended, Completed, Cancelled, Failed).
/// InternalsVisibleTo in Workflow.Infrastructure grants access to the internal members.
/// </summary>
public sealed class WorkflowTriggerServicePolicyTests
{
    private static readonly Guid _orgId     = Guid.NewGuid();
    private static readonly Guid _bindingId = Guid.NewGuid();
    private static readonly Guid _versionId = Guid.NewGuid();
    private static readonly DateTime _now   = DateTime.UtcNow;

    private static WorkflowInstance MakeInstance(WorkflowInstanceStatus status)
    {
        var i = WorkflowInstance.Start(_orgId, _bindingId, _versionId, "key", "entity", "Start", _now);
        switch (status)
        {
            case WorkflowInstanceStatus.Suspended: i.Suspend(_now); break;
            case WorkflowInstanceStatus.Completed: i.Complete(_now); break;
            case WorkflowInstanceStatus.Cancelled: i.Cancel(_now); break;
            case WorkflowInstanceStatus.Failed:    i.Fail("error", _now); break;
        }
        return i;
    }

    // ── StartNewInstance: always Start regardless of existing state ───────────

    [Theory]
    [InlineData(WorkflowInstanceStatus.Running)]
    [InlineData(WorkflowInstanceStatus.Suspended)]
    [InlineData(WorkflowInstanceStatus.Completed)]
    [InlineData(WorkflowInstanceStatus.Cancelled)]
    [InlineData(WorkflowInstanceStatus.Failed)]
    public void StartNewInstance_AnyExisting_ReturnsStart(WorkflowInstanceStatus status)
        => WorkflowTriggerService.DecideTriggerAction(
                WorkflowExecutionPolicy.StartNewInstance, MakeInstance(status))
            .Should().Be(WorkflowTriggerService.TriggerAction.Start);

    [Fact]
    public void StartNewInstance_NullExisting_ReturnsStart()
        => WorkflowTriggerService.DecideTriggerAction(
                WorkflowExecutionPolicy.StartNewInstance, null)
            .Should().Be(WorkflowTriggerService.TriggerAction.Start);

    // ── SignalExistingInstance: Signal when active, Skip otherwise ────────────

    [Theory]
    [InlineData(WorkflowInstanceStatus.Running)]
    [InlineData(WorkflowInstanceStatus.Suspended)]
    public void SignalExistingInstance_ActiveExisting_ReturnsSignal(WorkflowInstanceStatus status)
        => WorkflowTriggerService.DecideTriggerAction(
                WorkflowExecutionPolicy.SignalExistingInstance, MakeInstance(status))
            .Should().Be(WorkflowTriggerService.TriggerAction.Signal);

    [Theory]
    [InlineData(WorkflowInstanceStatus.Completed)]
    [InlineData(WorkflowInstanceStatus.Cancelled)]
    [InlineData(WorkflowInstanceStatus.Failed)]
    public void SignalExistingInstance_TerminalExisting_ReturnsSkip(WorkflowInstanceStatus status)
        => WorkflowTriggerService.DecideTriggerAction(
                WorkflowExecutionPolicy.SignalExistingInstance, MakeInstance(status))
            .Should().Be(WorkflowTriggerService.TriggerAction.Skip);

    [Fact]
    public void SignalExistingInstance_NullExisting_ReturnsSkip()
        => WorkflowTriggerService.DecideTriggerAction(
                WorkflowExecutionPolicy.SignalExistingInstance, null)
            .Should().Be(WorkflowTriggerService.TriggerAction.Skip);

    // ── StartIfNoRunningInstance: Skip when active, Start otherwise ──────────

    [Theory]
    [InlineData(WorkflowInstanceStatus.Running)]
    [InlineData(WorkflowInstanceStatus.Suspended)]
    public void StartIfNoRunning_ActiveExisting_ReturnsSkip(WorkflowInstanceStatus status)
        => WorkflowTriggerService.DecideTriggerAction(
                WorkflowExecutionPolicy.StartIfNoRunningInstance, MakeInstance(status))
            .Should().Be(WorkflowTriggerService.TriggerAction.Skip);

    [Theory]
    [InlineData(WorkflowInstanceStatus.Completed)]
    [InlineData(WorkflowInstanceStatus.Cancelled)]
    [InlineData(WorkflowInstanceStatus.Failed)]
    public void StartIfNoRunning_TerminalExisting_ReturnsStart(WorkflowInstanceStatus status)
        => WorkflowTriggerService.DecideTriggerAction(
                WorkflowExecutionPolicy.StartIfNoRunningInstance, MakeInstance(status))
            .Should().Be(WorkflowTriggerService.TriggerAction.Start);

    [Fact]
    public void StartIfNoRunning_NullExisting_ReturnsStart()
        => WorkflowTriggerService.DecideTriggerAction(
                WorkflowExecutionPolicy.StartIfNoRunningInstance, null)
            .Should().Be(WorkflowTriggerService.TriggerAction.Start);

    // ── RestartAfterTerminal: Skip when active, Start when null or terminal ──

    [Theory]
    [InlineData(WorkflowInstanceStatus.Running)]
    [InlineData(WorkflowInstanceStatus.Suspended)]
    public void RestartAfterTerminal_ActiveExisting_ReturnsSkip(WorkflowInstanceStatus status)
        => WorkflowTriggerService.DecideTriggerAction(
                WorkflowExecutionPolicy.RestartAfterTerminal, MakeInstance(status))
            .Should().Be(WorkflowTriggerService.TriggerAction.Skip);

    [Theory]
    [InlineData(WorkflowInstanceStatus.Completed)]
    [InlineData(WorkflowInstanceStatus.Cancelled)]
    [InlineData(WorkflowInstanceStatus.Failed)]
    public void RestartAfterTerminal_TerminalExisting_ReturnsStart(WorkflowInstanceStatus status)
        => WorkflowTriggerService.DecideTriggerAction(
                WorkflowExecutionPolicy.RestartAfterTerminal, MakeInstance(status))
            .Should().Be(WorkflowTriggerService.TriggerAction.Start);

    [Fact]
    public void RestartAfterTerminal_NullExisting_ReturnsStart()
        => WorkflowTriggerService.DecideTriggerAction(
                WorkflowExecutionPolicy.RestartAfterTerminal, null)
            .Should().Be(WorkflowTriggerService.TriggerAction.Start);
}
