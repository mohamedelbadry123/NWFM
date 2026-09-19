namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Results;
using global::Workflow.Application.Abstractions;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Infrastructure.Persistence;
using global::Workflow.Infrastructure.Persistence.Repositories;
using global::Workflow.Infrastructure.Services;

/// <summary>
/// Tests the WorkflowRuntimeEngine execution paths using an InMemory EF store.
/// Real repos are used for all EF-backed state; external services are mocked.
///
/// Case A: Start → ServiceTask (action success) → End         → instance Completed
/// Case B: Start → Timer (00:00:00 duration)  → End         → halts at timer, then
///         ResumeFromTimerAsync → instance Completed
/// Case C: Start → ParallelGateway → {ServiceTask1, ServiceTask2} → JoinGateway → End
///         → instance Completed after both branches complete
/// </summary>
public sealed partial class WorkflowRuntimeEnginePathTests : IDisposable
{
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly WorkflowDbContext _db;

    // Shared mocks reused across all three cases
    private readonly Mock<IWorkflowActionRegistry>        _actionRegistry   = new();
    private readonly Mock<IWorkflowNotificationPublisher> _notifPublisher    = new();
    private readonly Mock<IWorkflowOutcomeDispatcher>     _outcomeDispatcher = new();
    private readonly Mock<IBusinessCalendarService>       _calendarService   = new();
    private readonly Mock<IWorkflowIncidentService>       _incidentService   = new();
    private readonly Mock<IWorkflowRequestProjector>      _requestProjector  = new();
    private readonly Mock<IWorkflowAssignmentResolver>    _assignmentResolver = new();
    private readonly Mock<IWorkflowCandidateFactory>      _candidateFactory  = new();

    public WorkflowRuntimeEnginePathTests()
    {
        var opts = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseInMemoryDatabase($"EnginePathTests_{Guid.NewGuid()}")
            .Options;
        _db = new WorkflowDbContext(opts, new StubTenant(_orgId));

        // Outcome dispatcher: used when a workflow binding has Active/Shadow mode
        _outcomeDispatcher
            .Setup(d => d.DispatchAsync(It.IsAny<WorkflowOutcomeMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        // Request projector: no-ops for all calls
        _requestProjector
            .Setup(p => p.EnsureCreatedAsync(
                It.IsAny<WorkflowInstance>(), It.IsAny<WorkflowBinding>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _requestProjector
            .Setup(p => p.SyncCurrentTaskAsync(
                It.IsAny<WorkflowInstance>(), It.IsAny<WorkItem?>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    public void Dispose() => _db.Dispose();

    // ═══════════════════════════════════════════════════════════════════════
    // Case A: Start → ServiceTask → End
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CaseA_StartServiceTaskEnd_InstanceCompletes()
    {
        const string actionKey = "test.action.a";
        var now = DateTime.UtcNow;
        WorkflowActionExecutionContext? receivedContext = null;

        // Arrange: mock action provider returns success
        var actionProvider = new Mock<IWorkflowActionProvider>();
        actionProvider
            .Setup(p => p.ExecuteAsync(
                It.IsAny<WorkflowActionExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<WorkflowActionExecutionContext, CancellationToken>((context, _) => receivedContext = context)
            .ReturnsAsync(WorkflowActionExecutionResult.Succeeded());
        _actionRegistry.Setup(r => r.Resolve(actionKey)).Returns(actionProvider.Object);

        var (binding, versionId) = SeedWorkflowA(_db, _orgId, now, actionKey);
        var engine = BuildEngine(_db);

        // Act
        var result = await engine.StartAsync(
            _orgId, binding.Id, "entity-a", "idempotency-a",
            now, pinnedWorkflowVersionId: versionId);

        // Assert
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : "ok");
        result.Value.Status.Should().Be(WorkflowInstanceStatus.Completed,
            "ServiceTask succeeds so the engine advances to End and completes");
        receivedContext.Should().NotBeNull();
        receivedContext!.InputVariables["BusinessEntityId"].Should().Be("entity-a");
        receivedContext.InputVariables["OrganizationId"].Should().Be(_orgId.ToString());
    }

    private static (WorkflowBinding Binding, Guid VersionId) SeedWorkflowA(
        WorkflowDbContext db, Guid orgId, DateTime now, string actionKey)
    {
        var def = WorkflowDefinition.Create(orgId, "def-a", "Definition A", now);
        db.WorkflowDefinitions.Add(def);

        var version = WorkflowVersion.CreateDraft(def.Id, 1, Guid.NewGuid(), now);
        version.Publish(Guid.NewGuid(), now);
        db.WorkflowVersions.Add(version);

        var start   = ActivityDefinition.Create(version.Id, "start",  ActivityType.Start,       "Start",        now);
        var svc     = ActivityDefinition.Create(version.Id, "svc",    ActivityType.ServiceTask,  "Service Task", now, actionKey: actionKey);
        var end     = ActivityDefinition.Create(version.Id, "end",    ActivityType.End,          "End",          now);
        db.ActivityDefinitions.AddRange(start, svc, end);

        db.WorkflowTransitions.AddRange(
            WorkflowTransition.Create(version.Id, start.Id, svc.Id,  "t1", 1, now),
            WorkflowTransition.Create(version.Id, svc.Id,   end.Id,  "t2", 1, now));

        var binding = WorkflowBinding.Create(
            def.Id, orgId, "TestModule", "TestEntity", "Created", now,
            mode: WorkflowBindingMode.Active);
        db.WorkflowBindings.Add(binding);

        db.SaveChanges();
        return (binding, version.Id);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Case B: Start → Timer (0 duration) → End
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CaseB_StartTimerEnd_HaltsAtTimer_ThenResumesAndCompletes()
    {
        var now = DateTime.UtcNow;
        var (binding, versionId) = SeedWorkflowB(_db, _orgId, now);
        var engine = BuildEngine(_db);

        // Act – Start: should halt at the timer
        var startResult = await engine.StartAsync(
            _orgId, binding.Id, "entity-b", "idempotency-b",
            now, pinnedWorkflowVersionId: versionId);

        startResult.IsSuccess.Should().BeTrue(startResult.IsFailure ? startResult.Error.Message : "ok");
        var instance = startResult.Value;
        instance.Status.Should().Be(WorkflowInstanceStatus.Running,
            "engine halts at the Timer activity waiting for the timer to fire");

        // Find the timer in the DB (IgnoreQueryFilters in case filter oddities arise)
        var timer = await _db.WorkflowTimers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.WorkflowInstanceId == instance.Id);
        timer.Should().NotBeNull("timer must have been scheduled during StartAsync");
        timer!.Status.Should().Be(WorkflowTimerStatus.Pending);

        // Act – Resume (dueAt == now so DueAt > now is false → proceeds)
        var resumeResult = await engine.ResumeFromTimerAsync(timer.Id, now);

        resumeResult.IsSuccess.Should().BeTrue(resumeResult.IsFailure ? resumeResult.Error.Message : "ok");
        instance.Status.Should().Be(WorkflowInstanceStatus.Completed,
            "after the timer fires the engine advances past End and completes the instance");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Case D: Start → UserTask → End
    // Completing the work item must leave the UserTask, not recreate it.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CaseD_StartUserTaskEnd_CompleteAdvancesToEnd()
    {
        var now = DateTime.UtcNow;
        var groupId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        _assignmentResolver
            .Setup(r => r.ResolveGroupAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(groupId));
        _candidateFactory
            .Setup(c => c.CreateCandidatesAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WorkItemCandidate>());

        var (binding, versionId) = SeedWorkflowD(_db, _orgId, now);
        var engine = BuildEngine(_db);

        var startResult = await engine.StartAsync(
            _orgId, binding.Id, "entity-d", "idempotency-d",
            now, pinnedWorkflowVersionId: versionId);

        startResult.IsSuccess.Should().BeTrue(startResult.IsFailure ? startResult.Error.Message : "ok");
        var instance = startResult.Value;
        instance.Status.Should().Be(WorkflowInstanceStatus.Running);
        instance.CurrentActivityNodeKey.Should().Be("review");

        var item = await _db.WorkItems
            .IgnoreQueryFilters()
            .SingleAsync(w => w.WorkflowInstanceId == instance.Id);
        item.Claim(actorId, now);
        item.Complete(actorId, "APPROVE", now);
        await _db.SaveChangesAsync();

        var advance = await engine.AdvanceAsync(instance.Id, item.Id, now);

        advance.IsSuccess.Should().BeTrue(advance.IsFailure ? advance.Error.Message : "ok");
        instance.Status.Should().Be(WorkflowInstanceStatus.Completed,
            "completing the UserTask must take the outgoing to End, not recreate the same task");
        (await _db.WorkItems.IgnoreQueryFilters().CountAsync(w => w.WorkflowInstanceId == instance.Id))
            .Should().Be(1);
    }

    [Fact]
    public async Task UserTask_WithConditionalOutcomes_SelectsMatchingActionTransition()
    {
        var now = DateTime.UtcNow;
        var groupId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        _assignmentResolver
            .Setup(r => r.ResolveGroupAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(groupId));
        _candidateFactory
            .Setup(c => c.CreateCandidatesAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WorkItemCandidate>());

        var def = WorkflowDefinition.Create(_orgId, "conditional-user-task", "Conditional User Task", now);
        var version = WorkflowVersion.CreateDraft(def.Id, 1, actorId, now);
        version.Publish(actorId, now);
        var start = ActivityDefinition.Create(version.Id, "start", ActivityType.Start, "Start", now);
        var review = ActivityDefinition.Create(version.Id, "review", ActivityType.UserTask, "Review", now);
        var approved = ActivityDefinition.Create(version.Id, "approved", ActivityType.End, "Approved", now);
        var rejected = ActivityDefinition.Create(version.Id, "rejected", ActivityType.End, "Rejected", now);
        _db.AddRange(def, version, start, review, approved, rejected);
        _db.WorkflowTransitions.AddRange(
            WorkflowTransition.Create(version.Id, start.Id, review.Id, "start-review", 0, now, true),
            WorkflowTransition.Create(version.Id, review.Id, approved.Id, "approve", 1, now, false, "outcome == 'APPROVE'"),
            WorkflowTransition.Create(version.Id, review.Id, rejected.Id, "reject", 2, now, false, "outcome == 'REJECT'"));
        var binding = WorkflowBinding.Create(def.Id, _orgId, "TestModule", "TestEntity", "Created", now,
            mode: WorkflowBindingMode.Disabled, fixedWorkflowVersionId: version.Id);
        _db.WorkflowBindings.Add(binding);
        await _db.SaveChangesAsync();

        var engine = BuildEngine(_db);
        var started = await engine.StartAsync(_orgId, binding.Id, "conditional-entity", "conditional-instance",
            now, pinnedWorkflowVersionId: version.Id);
        var item = await _db.WorkItems.IgnoreQueryFilters().SingleAsync(x => x.WorkflowInstanceId == started.Value.Id);
        item.Claim(actorId, now);
        item.Complete(actorId, "APPROVE", now);
        await _db.SaveChangesAsync();

        var advanced = await engine.AdvanceAsync(started.Value.Id, item.Id, now);

        advanced.IsSuccess.Should().BeTrue(advanced.IsFailure ? advanced.Error.Message : "ok");
        started.Value.Status.Should().Be(WorkflowInstanceStatus.Completed);
        started.Value.CurrentActivityNodeKey.Should().Be("approved");
        (await _db.TransitionInstances.IgnoreQueryFilters()
            .SingleAsync(x => x.WorkflowInstanceId == started.Value.Id && x.FromActivityNodeKey == "review"))
            .TransitionKey.Should().Be("approve");
    }

    private static (WorkflowBinding Binding, Guid VersionId) SeedWorkflowD(
        WorkflowDbContext db, Guid orgId, DateTime now)
    {
        var def = WorkflowDefinition.Create(orgId, "def-d", "Definition D", now);
        db.WorkflowDefinitions.Add(def);

        var version = WorkflowVersion.CreateDraft(def.Id, 1, Guid.NewGuid(), now);
        version.Publish(Guid.NewGuid(), now);
        db.WorkflowVersions.Add(version);

        var start  = ActivityDefinition.Create(version.Id, "start",  ActivityType.Start,    "Start",  now);
        var review = ActivityDefinition.Create(version.Id, "review", ActivityType.UserTask, "Review", now);
        var end    = ActivityDefinition.Create(version.Id, "end",    ActivityType.End,      "End",    now);
        db.ActivityDefinitions.AddRange(start, review, end);

        db.WorkflowTransitions.AddRange(
            WorkflowTransition.Create(version.Id, start.Id,  review.Id, "t1", 1, now),
            WorkflowTransition.Create(version.Id, review.Id, end.Id,    "t2", 1, now));

        var binding = WorkflowBinding.Create(
            def.Id, orgId, "TestModule", "TestEntity", "Created", now,
            mode: WorkflowBindingMode.Active);
        db.WorkflowBindings.Add(binding);

        db.SaveChanges();
        return (binding, version.Id);
    }

    private static (WorkflowBinding Binding, Guid VersionId) SeedWorkflowB(
        WorkflowDbContext db, Guid orgId, DateTime now)
    {
        var def = WorkflowDefinition.Create(orgId, "def-b", "Definition B", now);
        db.WorkflowDefinitions.Add(def);

        var version = WorkflowVersion.CreateDraft(def.Id, 1, Guid.NewGuid(), now);
        version.Publish(Guid.NewGuid(), now);
        db.WorkflowVersions.Add(version);

        // Timer with zero duration so DueAt == StartTime (immediately eligible to fire)
        const string timerConfig = "{\"timerType\":\"Duration\",\"duration\":\"00:00:00\"}";
        var start = ActivityDefinition.Create(version.Id, "start", ActivityType.Start,   "Start", now);
        var timer = ActivityDefinition.Create(version.Id, "tmr",   ActivityType.Timer,   "Timer", now,
            configurationJson: timerConfig);
        var end   = ActivityDefinition.Create(version.Id, "end",   ActivityType.End,     "End",   now);
        db.ActivityDefinitions.AddRange(start, timer, end);

        db.WorkflowTransitions.AddRange(
            WorkflowTransition.Create(version.Id, start.Id, timer.Id, "t1", 1, now),
            WorkflowTransition.Create(version.Id, timer.Id, end.Id,   "t2", 1, now));

        var binding = WorkflowBinding.Create(
            def.Id, orgId, "TestModule", "TestEntity", "Created", now,
            mode: WorkflowBindingMode.Active);
        db.WorkflowBindings.Add(binding);

        db.SaveChanges();
        return (binding, version.Id);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Case C: Start → ParallelGateway → {ServiceTask1, ServiceTask2} → JoinGateway → End
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CaseC_ParallelServiceTasksJoin_InstanceCompletes()
    {
        const string actionKey = "test.action.c";
        var now = DateTime.UtcNow;

        // Mock: any service task action returns success
        var actionProvider = new Mock<IWorkflowActionProvider>();
        actionProvider
            .Setup(p => p.ExecuteAsync(
                It.IsAny<WorkflowActionExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkflowActionExecutionResult.Succeeded());
        _actionRegistry.Setup(r => r.Resolve(It.IsAny<string>())).Returns(actionProvider.Object);

        var (binding, versionId) = SeedWorkflowC(_db, _orgId, now, actionKey);
        var engine = BuildEngine(_db);

        // Act
        var result = await engine.StartAsync(
            _orgId, binding.Id, "entity-c", "idempotency-c",
            now, pinnedWorkflowVersionId: versionId);

        // Assert
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : "ok");
        result.Value.Status.Should().Be(WorkflowInstanceStatus.Completed,
            "both parallel branches complete before JoinGateway, which then advances to End");
    }

    [Fact]
    public async Task ParallelUserTasks_AdvanceTheirOwnBranches_AndJoinExactlyOnce()
    {
        var now = DateTime.UtcNow;
        var groupId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        _assignmentResolver
            .Setup(r => r.ResolveGroupAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(groupId));
        _candidateFactory
            .Setup(c => c.CreateCandidatesAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WorkItemCandidate>());

        var definition = WorkflowDefinition.Create(_orgId, "parallel-users", "Parallel user reviews", now);
        var version = WorkflowVersion.CreateDraft(definition.Id, 1, actorId, now);
        version.Publish(actorId, now);
        var start = ActivityDefinition.Create(version.Id, "start", ActivityType.Start, "Start", now);
        var fork = ActivityDefinition.Create(version.Id, "fork", ActivityType.ParallelGateway, "Fork", now,
            configurationJson: "{\"joinNodeKey\":\"join\"}");
        var reviewA = ActivityDefinition.Create(version.Id, "review-a", ActivityType.UserTask, "Review A", now);
        var reviewB = ActivityDefinition.Create(version.Id, "review-b", ActivityType.UserTask, "Review B", now);
        var join = ActivityDefinition.Create(version.Id, "join", ActivityType.JoinGateway, "Join", now);
        var end = ActivityDefinition.Create(version.Id, "end", ActivityType.End, "End", now);
        _db.AddRange(definition, version, start, fork, reviewA, reviewB, join, end);
        _db.WorkflowTransitions.AddRange(
            WorkflowTransition.Create(version.Id, start.Id, fork.Id, "start-fork", 0, now, true),
            WorkflowTransition.Create(version.Id, fork.Id, reviewA.Id, "branch-a", 1, now),
            WorkflowTransition.Create(version.Id, fork.Id, reviewB.Id, "branch-b", 2, now),
            WorkflowTransition.Create(version.Id, reviewA.Id, join.Id, "a-join", 1, now),
            WorkflowTransition.Create(version.Id, reviewB.Id, join.Id, "b-join", 1, now),
            WorkflowTransition.Create(version.Id, join.Id, end.Id, "join-end", 1, now));
        var binding = WorkflowBinding.Create(definition.Id, _orgId, "Test", "Case", "Created", now,
            mode: WorkflowBindingMode.Disabled, fixedWorkflowVersionId: version.Id);
        _db.WorkflowBindings.Add(binding);
        await _db.SaveChangesAsync();

        var engine = BuildEngine(_db);
        var started = await engine.StartAsync(_orgId, binding.Id, "parallel-case", "parallel-users-instance",
            now, pinnedWorkflowVersionId: version.Id);
        started.IsSuccess.Should().BeTrue(started.IsFailure ? started.Error.Message : "ok");
        var tasks = await _db.WorkItems.IgnoreQueryFilters().OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync();
        tasks.Should().HaveCount(2);

        tasks[0].Claim(actorId, now.AddMinutes(1));
        tasks[0].Complete(actorId, "APPROVE", now.AddMinutes(2));
        await _db.SaveChangesAsync();
        var first = await engine.AdvanceAsync(started.Value.Id, tasks[0].Id, now.AddMinutes(2));
        first.IsSuccess.Should().BeTrue(first.IsFailure ? first.Error.Message : "ok");
        started.Value.Status.Should().Be(WorkflowInstanceStatus.Running);

        tasks[1].Claim(actorId, now.AddMinutes(3));
        tasks[1].Complete(actorId, "APPROVE", now.AddMinutes(4));
        await _db.SaveChangesAsync();
        var second = await engine.AdvanceAsync(started.Value.Id, tasks[1].Id, now.AddMinutes(4));
        second.IsSuccess.Should().BeTrue(second.IsFailure ? second.Error.Message : "ok");
        started.Value.Status.Should().Be(WorkflowInstanceStatus.Completed);
        (await _db.WorkflowEvents.IgnoreQueryFilters()
            .CountAsync(x => x.WorkflowInstanceId == started.Value.Id && x.EventType == WorkflowEventType.JoinCompleted))
            .Should().Be(1);
        (await _db.WorkflowExecutionTokens.IgnoreQueryFilters()
            .CountAsync(x => x.WorkflowInstanceId == started.Value.Id && x.Status == ExecutionTokenStatus.Completed))
            .Should().Be(2);
    }

    private static (WorkflowBinding Binding, Guid VersionId) SeedWorkflowC(
        WorkflowDbContext db, Guid orgId, DateTime now, string actionKey)
    {
        var def = WorkflowDefinition.Create(orgId, "def-c", "Definition C", now);
        db.WorkflowDefinitions.Add(def);

        var version = WorkflowVersion.CreateDraft(def.Id, 1, Guid.NewGuid(), now);
        version.Publish(Guid.NewGuid(), now);
        db.WorkflowVersions.Add(version);

        var start   = ActivityDefinition.Create(version.Id, "start",  ActivityType.Start,           "Start",        now);
        var fork    = ActivityDefinition.Create(version.Id, "fork",   ActivityType.ParallelGateway,  "Fork",         now,
            configurationJson: "{\"joinNodeKey\":\"join\"}");
        var svc1    = ActivityDefinition.Create(version.Id, "svc1",   ActivityType.ServiceTask,      "Service 1",    now, actionKey: actionKey);
        var svc2    = ActivityDefinition.Create(version.Id, "svc2",   ActivityType.ServiceTask,      "Service 2",    now, actionKey: actionKey);
        var join    = ActivityDefinition.Create(version.Id, "join",   ActivityType.JoinGateway,      "Join",         now);
        var end     = ActivityDefinition.Create(version.Id, "end",    ActivityType.End,              "End",          now);
        db.ActivityDefinitions.AddRange(start, fork, svc1, svc2, join, end);

        db.WorkflowTransitions.AddRange(
            WorkflowTransition.Create(version.Id, start.Id, fork.Id,  "t-start-fork",  1, now),
            WorkflowTransition.Create(version.Id, fork.Id,  svc1.Id,  "t-fork-svc1",   1, now),
            WorkflowTransition.Create(version.Id, fork.Id,  svc2.Id,  "t-fork-svc2",   2, now),
            WorkflowTransition.Create(version.Id, svc1.Id,  join.Id,  "t-svc1-join",   1, now),
            WorkflowTransition.Create(version.Id, svc2.Id,  join.Id,  "t-svc2-join",   1, now),
            WorkflowTransition.Create(version.Id, join.Id,  end.Id,   "t-join-end",    1, now));

        var binding = WorkflowBinding.Create(
            def.Id, orgId, "TestModule", "TestEntity", "Created", now,
            mode: WorkflowBindingMode.Active);
        db.WorkflowBindings.Add(binding);

        db.SaveChanges();
        return (binding, version.Id);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Engine factory: real repos + real services + mocked external services
    // ═══════════════════════════════════════════════════════════════════════

    private WorkflowRuntimeEngine BuildEngine(WorkflowDbContext db, global::Workflow.Application.Integrations.IWorkflowIntegrationRuntime? integrations = null)
    {
        // Real DB-backed repositories
        var bindingRepo     = new WorkflowBindingRepository(db);
        var versionRepo     = new WorkflowVersionRepository(db);
        var definitionRepo  = new WorkflowDefinitionRepository(db);
        var instanceRepo    = new WorkflowInstanceRepository(db);
        var activityRepo    = new ActivityInstanceRepository(db);
        var workItemRepo    = new WorkItemRepository(db);
        var candidateRepo   = new WorkItemCandidateRepository(db);
        var variableRepo    = new WorkflowVariableRepository(db);
        var eventRepo       = new WorkflowEventRepository(db);
        var transitionRepo  = new TransitionInstanceRepository(db);
        var timerRepo       = new WorkflowTimerRepository(db);
        var tokenRepo       = new WorkflowExecutionTokenRepository(db);
        var slaRepo         = new SlaPolicyRepository(db);

        // Real stateless services
        var eventAppender    = new WorkflowEventAppender(eventRepo);
        var transitionEval   = new WorkflowTransitionEvaluator();
        var versionResolver  = new WorkflowVersionResolver(versionRepo);
        var timerService     = new WorkflowTimerService(timerRepo);

        return new WorkflowRuntimeEngine(
            db,
            bindingRepo,
            versionResolver,
            definitionRepo,
            versionRepo,
            instanceRepo,
            activityRepo,
            workItemRepo,
            candidateRepo,
            variableRepo,
            transitionEval,
            _assignmentResolver.Object,
            _candidateFactory.Object,
            slaRepo,
            _calendarService.Object,
            transitionRepo,
            eventAppender,
            eventRepo,
            _actionRegistry.Object,
            _incidentService.Object,
            timerService,
            timerRepo,
            _notifPublisher.Object,
            tokenRepo,
            _outcomeDispatcher.Object,
            _requestProjector.Object, integrations);
    }

    private sealed class StubTenant(Guid orgId) : ICurrentTenant
    {
        public Guid OrganizationId => orgId;
    }
}
