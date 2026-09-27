namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using NWFM.Shared.Integration.Workflow;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Infrastructure.Persistence.Repositories;

public sealed partial class WorkflowRuntimeEnginePathTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedService_StopsAtFailedNode_AndExplicitRetryExecutesAgain(bool retryable)
    {
        var now = DateTime.UtcNow;
        var contexts = new List<WorkflowActionExecutionContext>();
        var provider = new Mock<IWorkflowActionProvider>();
        provider.Setup(p => p.ExecuteAsync(It.IsAny<WorkflowActionExecutionContext>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowActionExecutionContext, CancellationToken>((context, _) => contexts.Add(context))
            .ReturnsAsync(() => contexts.Count == 1
                ? WorkflowActionExecutionResult.Failed("unavailable", "Service unavailable", retryable)
                : WorkflowActionExecutionResult.Succeeded());
        _actionRegistry.Setup(r => r.Resolve("test.http")).Returns(provider.Object);
        var (binding, version) = SeedSingleActivity(ActivityType.ServiceTask, """{"path":"/orders"}""", now, "test.http");
        var engine = BuildEngine(_db);

        var start = await engine.StartAsync(_orgId, binding.Id, "order", "retry", now, pinnedWorkflowVersionId: version);
        start.IsFailure.Should().BeTrue();
        var instance = await _db.WorkflowInstances.SingleAsync();
        instance.Status.Should().Be(WorkflowInstanceStatus.Failed);
        instance.CurrentActivityNodeKey.Should().Be("activity");
        (await _db.ActivityInstances.AnyAsync(a => a.ActivityType == ActivityType.End)).Should().BeFalse();

        var retry = await engine.AdvanceAsync(instance.Id, Guid.Empty, now.AddSeconds(1));
        retry.IsSuccess.Should().BeTrue();
        instance.Status.Should().Be(WorkflowInstanceStatus.Completed);
        instance.FailureReason.Should().BeNull();
        contexts.Should().HaveCount(2);
        contexts[0].ConfigurationJson.Should().Be("""{"path":"/orders"}""");
        contexts[0].IdempotencyKey.Should().Be(contexts[1].IdempotencyKey);
        contexts[0].ActivityInstanceId.Should().NotBeNull();
        contexts[0].ActivityInstanceId!.Value.Should().NotBe(contexts[1].ActivityInstanceId!.Value);
        var attempts = await _db.ActivityInstances.Where(a => a.ActivityNodeKey == "activity").ToListAsync();
        attempts.Should().ContainSingle(a => a.Status == ActivityInstanceStatus.Failed);
        attempts.Should().ContainSingle(a => a.Status == ActivityInstanceStatus.Completed);
    }

    [Theory]
    [InlineData("eventKey")]
    [InlineData("signalKey")]
    public async Task EventWait_AcceptsDesignerAndLegacyKeys(string property)
    {
        var now = DateTime.UtcNow;
        var (binding, version) = SeedSingleActivity(ActivityType.WaitEvent, $"{{\"{property}\":\"order.ready\"}}", now);
        var engine = BuildEngine(_db);
        var start = await engine.StartAsync(_orgId, binding.Id, "order", "wait", now, pinnedWorkflowVersionId: version);
        start.IsSuccess.Should().BeTrue();
        start.Value.Status.Should().Be(WorkflowInstanceStatus.Running);

        (await engine.ResumeFromExternalSignalAsync(start.Value.Id, "order.ready", now.AddSeconds(1))).IsSuccess.Should().BeTrue();
        start.Value.Status.Should().Be(WorkflowInstanceStatus.Completed);
        (await engine.ResumeFromExternalSignalAsync(start.Value.Id, "order.ready", now.AddSeconds(2))).IsFailure.Should().BeTrue();
        (await _db.ActivityInstances.CountAsync(a => a.ActivityType == ActivityType.End)).Should().Be(1);
    }

    [Theory]
    [InlineData("{\"eventKey\":\"order.ready\"}", "another.event")]
    [InlineData("{\"eventKey\":\"order.ready\"}", null)]
    [InlineData("{\"eventKey\":\"order.ready\"}", "")]
    [InlineData("{}", "order.ready")]
    [InlineData("[]", "order.ready")]
    [InlineData("{\"eventKey\":123}", "order.ready")]
    [InlineData("{\"eventKey\":\"\",\"signalKey\":\"order.ready\"}", "order.ready")]
    public async Task EventWait_InvalidOrMissingKey_DoesNotConsumeWait(string configuration, string? signal)
    {
        var now = DateTime.UtcNow;
        var (binding, version) = SeedSingleActivity(ActivityType.WaitEvent, configuration, now);
        var engine = BuildEngine(_db);
        var start = await engine.StartAsync(_orgId, binding.Id, "order", "wait", now, pinnedWorkflowVersionId: version);

        var result = await engine.ResumeFromExternalSignalAsync(start.Value.Id, signal, now.AddSeconds(1));
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Workflow.Event.KeyMismatch");
        start.Value.Status.Should().Be(WorkflowInstanceStatus.Running);
        (await _db.ActivityInstances.SingleAsync(a => a.ActivityType == ActivityType.WaitEvent)).Status
            .Should().Be(ActivityInstanceStatus.Active);
    }

    [Fact]
    public async Task EventWait_WithoutActiveActivity_DoesNotAdvance()
    {
        var now = DateTime.UtcNow;
        var (binding, version) = SeedSingleActivity(ActivityType.WaitEvent, """{"eventKey":"ready"}""", now);
        var engine = BuildEngine(_db);
        var start = await engine.StartAsync(_orgId, binding.Id, "order", "wait", now, pinnedWorkflowVersionId: version);
        (await _db.ActivityInstances.SingleAsync(a => a.ActivityType == ActivityType.WaitEvent)).Complete(now);
        await _db.SaveChangesAsync();

        var result = await engine.ResumeFromExternalSignalAsync(start.Value.Id, "ready", now.AddSeconds(1));
        result.Error.Code.Should().Be("Workflow.Event.NoActiveWait");
        start.Value.Status.Should().Be(WorkflowInstanceStatus.Running);
    }

    [Fact]
    public async Task RetryOperation_CannotSkipAnActiveWait()
    {
        var now = DateTime.UtcNow;
        var (binding, version) = SeedSingleActivity(ActivityType.WaitEvent, """{"eventKey":"ready"}""", now);
        var engine = BuildEngine(_db);
        var start = await engine.StartAsync(_orgId, binding.Id, "order", "wait", now, pinnedWorkflowVersionId: version);
        var retry = await engine.AdvanceAsync(start.Value.Id, Guid.Empty, now.AddSeconds(1));
        retry.IsFailure.Should().BeTrue();
        (await _db.ActivityInstances.SingleAsync(a => a.ActivityType == ActivityType.WaitEvent)).Status
            .Should().Be(ActivityInstanceStatus.Active);
    }

    [Fact]
    public async Task ExternalSignalTimer_IsExcludedFromDueBatch_AndCannotFireByClock()
    {
        var now = DateTime.UtcNow;
        var (binding, version) = SeedSingleActivity(ActivityType.Timer,
            """{"timerType":"ExternalSignal","signalKey":"ready","duration":"00:00:00"}""", now);
        var engine = BuildEngine(_db);
        var start = await engine.StartAsync(_orgId, binding.Id, "order", "timer", now, pinnedWorkflowVersionId: version);
        var timer = await _db.WorkflowTimers.SingleAsync();
        var repository = new WorkflowTimerRepository(_db);
        (await repository.GetPendingDueAsync(now.AddDays(1), 100)).Should().BeEmpty();
        var result = await engine.ResumeFromTimerAsync(timer.Id, now.AddDays(1));
        result.Error.Code.Should().Be("Workflow.Timer.ExternalSignalRequired");
        timer.Status.Should().Be(WorkflowTimerStatus.Pending);
        start.Value.Status.Should().Be(WorkflowInstanceStatus.Running);
    }

    [Theory]
    [InlineData("{\"setVariables\":{\"BusinessEntityId\":\"updated\"}}")]
    [InlineData("{\"setVariables\":[{\"name\":\"BusinessEntityId\",\"value\":\"updated\"}]}")]
    [InlineData("{\"assignments\":{\"BusinessEntityId\":\"updated\"}}")]
    public async Task SetVariables_AppliesDesignerAndLegacyAssignments(string configuration)
    {
        var now = DateTime.UtcNow;
        var (binding, version) = SeedSingleActivity(ActivityType.ScriptTask, configuration, now);
        var start = await BuildEngine(_db).StartAsync(_orgId, binding.Id, "original", "variables", now, pinnedWorkflowVersionId: version);
        start.IsSuccess.Should().BeTrue();
        start.Value.Status.Should().Be(WorkflowInstanceStatus.Completed);
        (await _db.WorkflowVariables.SingleAsync(v => v.VariableName == "BusinessEntityId")).ValueJson.Should().Be("\"updated\"");
    }

    [Theory]
    [InlineData("2026-09-19T15:30:00+03:00")]
    [InlineData("2026-09-19T12:30:00Z")]
    public async Task DueDateTimer_PreservesTheConfiguredInstant(string dueAt)
    {
        var now = new DateTime(2026, 9, 19, 10, 0, 0, DateTimeKind.Utc);
        var (binding, version) = SeedSingleActivity(ActivityType.Timer,
            $"{{\"timerType\":\"DueDate\",\"dueAt\":\"{dueAt}\"}}", now);
        var start = await BuildEngine(_db).StartAsync(_orgId, binding.Id, "order", "date", now, pinnedWorkflowVersionId: version);
        start.IsSuccess.Should().BeTrue();
        (await _db.WorkflowTimers.SingleAsync()).DueAt.Should().Be(new DateTime(2026, 9, 19, 12, 30, 0, DateTimeKind.Utc));
    }

    private (WorkflowBinding Binding, Guid Version) SeedSingleActivity(ActivityType type, string configuration, DateTime now, string? action = null)
    {
        var definition = WorkflowDefinition.Create(_orgId, "improvement", "Improvement regression", now);
        var version = WorkflowVersion.CreateDraft(definition.Id, 1, Guid.NewGuid(), now);
        version.Publish(Guid.NewGuid(), now);
        var start = ActivityDefinition.Create(version.Id, "start", ActivityType.Start, "Start", now);
        var activity = ActivityDefinition.Create(version.Id, "activity", type, "Activity", now, actionKey: action, configurationJson: configuration);
        var end = ActivityDefinition.Create(version.Id, "end", ActivityType.End, "End", now);
        var binding = WorkflowBinding.Create(definition.Id, _orgId, "Test", "Order", "Created", now, mode: WorkflowBindingMode.Active);
        _db.WorkflowDefinitions.Add(definition);
        _db.WorkflowVersions.Add(version);
        _db.ActivityDefinitions.AddRange(start, activity, end);
        _db.WorkflowTransitions.AddRange(
            WorkflowTransition.Create(version.Id, start.Id, activity.Id, "enter", 1, now),
            WorkflowTransition.Create(version.Id, activity.Id, end.Id, "exit", 1, now));
        _db.WorkflowBindings.Add(binding);
        _db.SaveChanges();
        return (binding, version.Id);
    }
}
