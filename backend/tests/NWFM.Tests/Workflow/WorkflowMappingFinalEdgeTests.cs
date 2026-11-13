namespace NWFM.Tests.Modules.Workflow;

using System.Text.Json;
using FluentAssertions;
using global::FormEngine.Application.Common.Interfaces;
using global::FormEngine.Application.Common.Schema;
using global::FormEngine.Application.Forms.Common;
using global::FormEngine.Application.Submissions.Common;
using global::FormEngine.Domain.Constants;
using global::FormEngine.Domain.Entities;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.Commands.CompleteWorkItem;
using global::Workflow.Application.Integrations;
using global::Workflow.Application.Mapping;
using global::Workflow.Application.Workspace;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Domain.Repositories;
using global::Workflow.Infrastructure.Persistence;
using global::Workflow.Infrastructure.Persistence.Repositories;
using global::Workflow.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;
using Moq;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Results;

public sealed partial class WorkflowRuntimeEnginePathTests
{
    [Theory]
    [InlineData("Latest")]
    [InlineData("Attention")]
    public async Task FinalEdgeSelectorDeadlineFiltersBeforeLatestAndAmbiguity(string multiple)
    {
        var s = MappingServices(); using var local = new MappingLocalServer(); var connection = await Connection(s.Api, "Http", local.Address, "None");
        var seed = await TypedSeed(connection); var anchor = DateTime.UtcNow.AddSeconds(-25); var actor = Guid.NewGuid();
        var mapping = EmptyMapping() with { Selectors = [new("prior", seed.Version.WorkflowDefinitionId, seed.Version.Id, "BusinessKey", Literal("customer"), MultipleMatches: multiple, Missing: new("Wait", 10, "expired"))] };
        var owner = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "owner", "customer", "activity", anchor, startedByUserId: actor);
        var activity = ActivityInstance.Start(_orgId, owner.Id, "activity", ActivityType.ServiceTask, "Event", anchor);
        var invocation = WorkflowMappingInvocation.Create(_orgId, owner.Id, activity.Id, null, actor, owner.Id, seed.Version.Id, "activity", "Callable",
            Config(new HttpActivityConfiguration { ConnectionId = connection, MappingMode = "Typed", TypedMapping = mapping }), "deadline-filter", anchor);
        _db.AddRange(owner, activity, invocation); await _db.SaveChangesAsync(); await s.Runtime.PrepareAsync(invocation, default);
        invocation.Status.Should().Be("Waiting"); var frozen = invocation.CriteriaJson;
        var timely = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "X", "customer", "activity", anchor.AddSeconds(5));
        var late = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "Y", "customer", "activity", anchor.AddSeconds(20));
        _db.AddRange(timely, late); await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();
        await s.Runtime.RecoverAsync(default); invocation = await _db.MappingInvocations.SingleAsync();
        invocation.Status.Should().Be("Prepared"); invocation.CriteriaJson.Should().Be(frozen);
        invocation.SelectionsJson.Should().Contain(timely.Id.ToString()).And.NotContain(late.Id.ToString()).And.NotContain(owner.Id.ToString());
        await new WorkflowIntegrationProcessor(_db, s.Api, new WorkflowIntegrationTransport(), Mock.Of<IWorkflowRuntimeEngine>(), mappings: s.Runtime).ProcessJobAsync((await _db.IntegrationJobs.SingleAsync()).Id, default);
        local.Bodies.Should().ContainSingle();
    }

    private sealed class RepeatedRecoveryConflict(Guid invocationId, int limit) : SaveChangesInterceptor
    {
        public int Failures { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Failures < limit && eventData.Context!.ChangeTracker.Entries<WorkflowMappingInvocation>().Any(e => e.Entity.Id == invocationId && e.State == EntityState.Modified))
            { Failures++; throw new DbUpdateConcurrencyException("Injected rowversion conflict in preparation and diagnostic persistence."); }
            return ValueTask.FromResult(result);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task FinalEdgeTransientRecoveryAndSecondDiagnosticConflictDoNotStarveHealthyRow(int conflictsToInject)
    {
        var s = MappingServices(); using var local = new MappingLocalServer(); var connection = await Connection(s.Api, "Http", local.Address, "None"); var seed = await TypedSeed(connection); var now = DateTime.UtcNow;
        var actor = Guid.NewGuid(); var root = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "transient", "key", "activity", now, startedByUserId: actor);
        var activity = ActivityInstance.Start(_orgId, root.Id, "activity", ActivityType.ServiceTask, "Event", now);
        var retry = WorkflowMappingInvocation.Create(_orgId, root.Id, activity.Id, null, actor, root.Id, seed.Version.Id, "activity", "Callable", seed.Node.ConfigurationJson!, "retry", now);
        var healthy = WorkflowMappingInvocation.Create(_orgId, root.Id, activity.Id, null, actor, root.Id, seed.Version.Id, "activity", "Callable", seed.Node.ConfigurationJson!, "healthy", now);
        _db.AddRange(root, activity, retry, healthy); await _db.SaveChangesAsync();
        var conflicts = new RepeatedRecoveryConflict(retry.Id, conflictsToInject);
        var options = new DbContextOptionsBuilder<WorkflowDbContext>((DbContextOptions<WorkflowDbContext>)_db.GetService<IDbContextOptions>()).AddInterceptors(conflicts).Options;
        await using var db = new WorkflowDbContext(options, new StubTenant(_orgId));
        var runtime = new WorkflowMappingRuntime(db, new MappingSourceResolver(db, s.Access.Object, s.Forms.Object), s.Services.Object);
        await runtime.RecoverAsync(default); conflicts.Failures.Should().Be(conflictsToInject);
        var deferred = await db.MappingInvocations.SingleAsync(i => i.Id == retry.Id); deferred.Status.Should().Be("Preparing");
        if (conflictsToInject == 1) { deferred.Error.Should().Contain("retry pending"); deferred.UpdatedAt.Should().BeAfter(retry.UpdatedAt); }
        (await db.MappingInvocations.SingleAsync(i => i.Id == healthy.Id)).Status.Should().Be("Prepared");
        var processor = new WorkflowIntegrationProcessor(db, s.Api, new WorkflowIntegrationTransport(), Mock.Of<IWorkflowRuntimeEngine>(), mappings: runtime);
        await processor.ProcessJobAsync((await db.IntegrationJobs.SingleAsync()).Id, default);
        await runtime.RecoverAsync(default);
        await processor.ProcessJobAsync((await db.IntegrationJobs.SingleAsync(j => j.InvocationId == retry.Id)).Id, default);
        (await db.MappingInvocations.ToListAsync()).Should().OnlyContain(i => i.Applied && i.Status == "Completed"); local.Bodies.Should().HaveCount(2);
        WorkflowExecutionLock.IsTransient(db, new TimeoutException()).Should().BeTrue();
        WorkflowExecutionLock.IsTransient(db, new InvalidOperationException("invalid graph")).Should().BeFalse();
    }

    [Fact]
    public async Task FinalEdgeOnFailureQueueRejectionPreservesPrimaryTerminalFailure()
    {
        var s = MappingServices(); using var local = new MappingLocalServer(); var connection = await Connection(s.Api, "Http", local.Address, "None"); var now = DateTime.UtcNow;
        var seed = SeedSingleActivity(ActivityType.Timer, "{\"duration\":\"01:00:00\"}", now);
        var version = (await _db.WorkflowVersions.FindAsync(seed.Version))!; version.SetWorkspace(Config(new WorkflowWorkspaceDefinition("Main", "C", "B", DesignerVersion: 4)));
        _db.AddRange(ActivityDefinition.Create(seed.Version, "attached", ActivityType.ServiceTask, "Attached", now, actionKey: "http.request", configurationJson: Config(new
        { connectionId = connection, mappingMode = "Typed", typedMapping = EmptyMapping() with { Endpoint = EmptyMapping().Endpoint! with { Response = new("string") } }, required = false, triggerBinding = new { sourceNodeKey = "activity", trigger = "OnComment" } })),
            ActivityDefinition.Create(seed.Version, "invalid-handler", ActivityType.ServiceTask, "Invalid OnFailure", now, actionKey: "http.request", configurationJson: Config(new
            { connectionId = connection, mappingMode = "Typed", required = false, triggerBinding = new { sourceNodeKey = "activity", trigger = "OnFailure" } })));
        await _db.SaveChangesAsync(); var engine = BuildEngine(_db, s.Api, stepMappings: s.Runtime);
        var root = (await engine.StartAsync(_orgId, seed.Binding.Id, "order", "handler-rejection", now, startedByUserId: Guid.NewGuid(), pinnedWorkflowVersionId: version.Id)).Value;
        var origin = await _db.ActivityInstances.SingleAsync(a => a.ActivityNodeKey == "activity"); var definition = await _db.ActivityDefinitions.SingleAsync(a => a.WorkflowVersionId == version.Id && a.NodeKey == "activity");
        var events = new WorkflowActivityEvents(_db, s.Api, s.Runtime); await events.QueueAsync(root, definition, origin, "OnComment", "one", default);
        var processor = new WorkflowIntegrationProcessor(_db, s.Api, new WorkflowIntegrationTransport(), engine, events, s.Runtime); var job = await _db.IntegrationJobs.SingleAsync();
        await processor.ProcessJobAsync(job.Id, default); await s.Runtime.RecoverAsync(default); await processor.ProcessJobAsync(job.Id, default);
        var invocation = await _db.MappingInvocations.SingleAsync(); invocation.Status.Should().Be("Attention"); invocation.Error.Should().Contain("Output application:").And.Contain("OnFailure dispatch failed");
        job.Status.Should().Be("Failed"); job.Error.Should().Contain("OnFailure dispatch failed");
        origin.Status.Should().Be(ActivityInstanceStatus.Active); (await _db.RoutingResults.CountAsync()).Should().Be(0); local.Bodies.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinalEdgeUntypedWorkItemSubmissionCannotPreclaimAndClaimantCompletionReplays(bool multi)
    {
        var now = DateTime.UtcNow; var claimant = Guid.NewGuid(); var attacker = Guid.NewGuid(); var activeActor = attacker.ToString();
        using var formsDb = global::NWFM.Tests.Modules.FormEngine.FormEngineTestData.CreateContext();
        var store = new Mock<IFormSubmissionStore>(); var keys = new Dictionary<Guid, Guid>(); var rows = new Dictionary<Guid, IReadOnlyDictionary<string, object?>>();
        var publisher = new FormPublisher(formsDb, store.Object, Mock.Of<NWFM.Shared.Caching.ICacheService>(), TimeProvider.System);
        var form = FormDefinition.Create("EDGE", "Edge", "Edge", FormCategories.Inspection, null, null, "test", now);
        form.SetSchema(global::NWFM.Tests.Modules.FormEngine.FormEngineTestData.SimpleSchema(), null, null, "test", now); formsDb.FormDefinitions.Add(form); await formsDb.SaveChangesAsync();
        (await publisher.PublishAsync(form.Id, "test", default)).IsSuccess.Should().BeTrue(); store.Invocations.Clear();
        store.Setup(s => s.FindByClientIdAsync(It.IsAny<FormTable>(), It.IsAny<Guid>(), default)).ReturnsAsync((FormTable _, Guid id, CancellationToken _) => keys.TryGetValue(id, out var receipt) ? (Guid?)receipt : null);
        store.Setup(s => s.GetByIdAsync(It.IsAny<FormTable>(), It.IsAny<Guid>(), default)).ReturnsAsync((FormTable _, Guid id, CancellationToken _) => rows.GetValueOrDefault(id));
        store.Setup(s => s.InsertAsync(It.IsAny<FormTable>(), It.IsAny<FormSubmissionInsert>(), default)).ReturnsAsync((FormTable _, FormSubmissionInsert input, CancellationToken _) =>
        {
            var id = Guid.NewGuid(); keys.Add(input.ClientSubmissionId!.Value, id);
            rows.Add(id, new Dictionary<string, object?>
            {
                ["Id"] = id,
                ["VersionNo"] = input.VersionNo,
                ["ContextType"] = input.ContextType,
                ["ContextId"] = input.ContextId,
                ["SubmittedBy"] = input.SubmittedBy,
                ["SubmittedDate"] = DateTimeOffset.UtcNow,
                ["meter_reading"] = input.Answers["meter_reading"]
            });
            return id;
        });
        var config = new BusinessActivityConfiguration
        {
            FormDefinitionId = multi ? null : form.Id,
            FormVersionNo = multi ? null : 1,
            FormSteps = multi ? [new() { Id = "first", FormId = form.Id, VersionNo = 1 }, new() { Id = "second", FormId = form.Id, VersionNo = 1 }] : []
        };
        var seed = SeedSingleActivity(ActivityType.UserTask, Config(config), now); var versions = new WorkflowVersionRepository(_db);
        var version = (await versions.GetByIdWithProjectionAsync(seed.Version))!; version.SetWorkspace(null);
        var root = WorkflowInstance.Start(_orgId, seed.Binding.Id, version.Id, "guard", "key", "activity", now);
        var activity = ActivityInstance.Start(_orgId, root.Id, "activity", ActivityType.UserTask, "Step", now);
        var item = WorkItem.Create(_orgId, root.Id, activity.Id, Guid.NewGuid(), now); _db.AddRange(root, activity, item); await _db.SaveChangesAsync();
        var user = new Mock<NWFM.Shared.Abstractions.ICurrentUser>(); user.SetupGet(u => u.Id).Returns(() => activeActor);
        var guard = new WorkflowFormSubmissionGuard(_db);
        var service = new FormSubmissionService(formsDb, store.Object, publisher, Mock.Of<NWFM.Shared.Storage.IFileStorage>(), user.Object, TimeProvider.System,
            Options.Create(new NWFM.Shared.Options.FileStorageOptions()), Options.Create(new global::FormEngine.Domain.Options.FormEngineOptions()), [guard]);
        var gateway = new global::FormEngine.Infrastructure.Integration.FormGateway(formsDb, store.Object, service, Mock.Of<NWFM.Shared.Storage.IFileStorage>());
        var request = new FormSubmissionRequest
        {
            FormDefinitionId = form.Id,
            VersionNo = 1,
            ContextType = "WorkItem",
            ContextId = multi ? item.Id + ":" + item.Id : item.Id.ToString(),
            ClientSubmissionId = item.Id,
            Answers = new Dictionary<string, object?> { ["meter_reading"] = 999 }
        };
        (await service.SubmitAsync(request, default)).Error.Code.Should().Be("Workflow.Form.Context");
        item.Claim(claimant, now); await _db.SaveChangesAsync();
        (await service.SubmitAsync(request, default)).Error.Code.Should().Be("Workflow.Form.Context");
        (await service.SubmitAsync(request with { ContextType = "Standalone", ContextId = null }, default)).IsFailure.Should().BeTrue();
        store.Verify(s => s.InsertAsync(It.IsAny<FormTable>(), It.IsAny<FormSubmissionInsert>(), default), Times.Never);
        activeActor = claimant.ToString();
        request = request with { ClientSubmissionId = item.FormReceiptKey(item.Id, claimant) };
        (await service.SubmitAsync(request with { VersionNo = 2 }, default)).IsFailure.Should().BeTrue();
        var engine = new Mock<IWorkflowRuntimeEngine>(); engine.Setup(e => e.AdvanceAsync(root.Id, item.Id, It.IsAny<DateTime>(), default)).ReturnsAsync(Result.Success());
        var gate = new Mock<IWorkflowFeatureGate>(); gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
        var handler = new CompleteWorkItemCommandHandler(gate.Object, new WorkItemRepository(_db), engine.Object, new WorkflowEventAppender(new WorkflowEventRepository(_db)),
            Mock.Of<IWorkItemDtoAssembler>(), new WorkflowInstanceRepository(_db), versions, new ActivityInstanceRepository(_db), Mock.Of<IWorkflowAssignmentGroupRepository>(),
            Mock.Of<IWorkflowDepartmentRepository>(), Mock.Of<IWorkflowCandidateFactory>(), Mock.Of<IWorkItemCandidateRepository>(), gateway);
        var command = new CompleteWorkItemCommand(item.Id, claimant, _orgId, "APPROVE", FormValues: new() { ["meter_reading"] = JsonSerializer.SerializeToElement(42) }, FormVisitId: multi ? item.Id : null);
        var result = await handler.Handle(command, default); result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : "");
        var replay = await service.SubmitAsync(request, default); replay.IsSuccess.Should().BeTrue(); replay.Value.IsReplay.Should().BeTrue(); keys.Should().ContainSingle();
        var receipt = await gateway.GetByClientIdAsync(form.Id, item.FormReceiptKey(item.Id, claimant), "WorkItem", request.ContextId!, default); receipt!.SubmittedBy.Should().Be(claimant.ToString());
        JsonSerializer.SerializeToElement(receipt.Answers["meter_reading"]).GetInt32().Should().Be(42);
        if (multi)
        {
            var visit = ActivityFormProgress.Read(item.FormProgressJson, config.FormSteps, item.Id).VisitId;
            (await handler.Handle(command with { FormVisitId = visit }, default)).IsSuccess.Should().BeTrue(); keys.Should().HaveCount(2);
            (await service.SubmitAsync(request, default)).Value.IsReplay.Should().BeTrue();
        }
        item.Status.Should().Be(WorkItemStatus.Completed);
        activeActor = attacker.ToString(); (await service.SubmitAsync(request, default)).IsFailure.Should().BeTrue();
        // A standalone form with its own key retains ordinary FormEngine behavior.
        (await service.SubmitAsync(request with { ContextType = "Standalone", ContextId = null, ClientSubmissionId = Guid.NewGuid() }, default)).IsSuccess.Should().BeTrue();
        store.Verify(s => s.GetLatestByContextAsync(It.IsAny<FormTable>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinalEdgeCompletedFormReceiptFromAnotherActorIsForbidden(bool multi)
    {
        using var local = new MappingLocalServer(); var f = await IndependentFormSource(local.Address, DateTime.UtcNow, false);
        var visit = multi ? Guid.NewGuid() : f.Item.Id; var submission = Guid.NewGuid(); var source = f.Source;
        if (multi)
        {
            var node = await _db.ActivityDefinitions.SingleAsync(n => n.WorkflowVersionId == source.WorkflowVersionId && n.NodeKey == source.NodeKey);
            var business = IntegrationJson.Read<BusinessActivityConfiguration>(node.ConfigurationJson);
            business.FormDefinitionId = null; business.FormVersionNo = null; business.FormSteps = [new() { Id = "first", FormId = source.FormId!.Value, VersionNo = 1 }];
            node.SetPublishedConfiguration(Config(business)); source = source with { FormStepId = "first" };
            f.Item.SetFormProgress(JsonSerializer.Serialize(new ActivityFormProgress
            {
                ActiveStepId = "first",
                VisitId = visit,
                ExitNodeKey = "done",
                Visits = [new() { VisitId = visit, StepId = "first", FormId = source.FormId!.Value, VersionNo = 1, ActorId = f.Invocation.ActorUserId, SubmissionId = submission }]
            }));
        }
        f.Item.Complete(f.Invocation.ActorUserId, "done", DateTime.UtcNow); await _db.SaveChangesAsync();
        f.Forms.Setup(g => g.GetByClientIdAsync(source.FormId!.Value, visit, "WorkItem", multi ? f.Item.Id + ":" + visit : f.Item.Id.ToString(), default))
            .ReturnsAsync(new FormSubmissionRecord(submission, source.FormId!.Value, 1, Guid.NewGuid().ToString(), "Attacker", DateTimeOffset.UtcNow, new Dictionary<string, object?> { ["text"] = "forged" }));
        Func<Task> read = () => f.Resolver.ResolveAsync(source, new(_orgId, f.Invocation.ActorUserId, f.Invocation.RootInstanceId, f.Invocation.Id, DateTimeOffset.UtcNow,
            new Dictionary<string, FrozenRelatedSelection>()), default);
        await read.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("*completion actor*");
        (await _db.IntegrationJobs.CountAsync()).Should().Be(0); local.Bodies.Should().BeEmpty();
    }
}
