namespace NWFM.Tests.Modules.Workflow;

using System.Text.Json;
using FluentAssertions;
using global::FormEngine.Application.Common.Interfaces;
using global::FormEngine.Application.Submissions.Common;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.Commands.CompleteWorkItem;
using global::Workflow.Application.Integrations;
using global::Workflow.Application.Mapping;
using global::Workflow.Application.Workspace;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Domain.Repositories;
using global::Workflow.Infrastructure.Persistence.Repositories;
using global::Workflow.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using NWFM.Shared.Constants;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Organization;

public sealed partial class WorkflowRuntimeEnginePathTests
{
    private async Task AssertReadonlySubmissionPathsRejectVariants(WorkItem item, Guid formId, Guid actor, WorkflowFormSubmissionGuard guard)
    {
        var store = new Mock<IFormSubmissionStore>(MockBehavior.Strict);
        var service = new FormSubmissionService(Mock.Of<IFormEngineDbContext>(), store.Object, Mock.Of<IFormPublisher>(),
            Mock.Of<NWFM.Shared.Storage.IFileStorage>(), Mock.Of<NWFM.Shared.Abstractions.ICurrentUser>(u => u.Id == actor.ToString()), TimeProvider.System,
            Options.Create(new NWFM.Shared.Options.FileStorageOptions()), Options.Create(new global::FormEngine.Domain.Options.FormEngineOptions()), [guard]);
        var variants = new Dictionary<string, object?> { [" A.B "] = 999, ["a.b"] = 42, ["editable"] = "edit" };
        var generic = await service.SubmitAsync(new()
        {
            FormDefinitionId = formId,
            VersionNo = 2,
            ContextType = "WorkItem",
            ContextId = item.Id + ":" + item.Id,
            ClientSubmissionId = item.FormReceiptKey(item.Id, actor),
            Answers = variants
        }, default);
        generic.IsFailure.Should().BeTrue(); generic.Error.Code.Should().Be("Workflow.Form.Context");
        var forms = new Mock<IFormGateway>(); forms.Setup(f => f.SubmitAsync(It.IsAny<FormSubmitRequest>(), default)).Returns(async (FormSubmitRequest r, CancellationToken ct) =>
        {
            var result = await service.SubmitAsync(new() { FormDefinitionId = r.FormId, VersionNo = r.VersionNo, ContextType = r.ContextType, ContextId = r.ContextId, ClientSubmissionId = r.ClientSubmissionId, Answers = r.Answers }, ct);
            return NWFM.Shared.Results.Result.Failure<FormSubmitReceipt>(result.Error);
        });
        forms.Setup(f => f.GetVersionSchemaAsync(formId, 2, default)).ReturnsAsync("{}");
        forms.Setup(f => f.GetFieldsAsync(formId, 2, default)).ReturnsAsync([]);
        var gate = new Mock<IWorkflowFeatureGate>(); gate.Setup(g => g.EnsureEnabled()).Returns(NWFM.Shared.Results.Result.Success());
        var groups = new Mock<IWorkflowAssignmentGroupRepository>(); groups.Setup(g => g.IsUserMemberOfGroupAsync(item.AssignmentGroupId, actor, _orgId, default)).ReturnsAsync(true);
        var engine = new Mock<IWorkflowRuntimeEngine>(MockBehavior.Strict);
        var roles = new Mock<IWorkflowRoleAssignments>(); roles.Setup(r => r.CanAccessAsync(item, actor, default)).ReturnsAsync(true);
        var handler = new CompleteWorkItemCommandHandler(gate.Object, new WorkItemRepository(_db), engine.Object, Mock.Of<IWorkflowEventAppender>(), Mock.Of<IWorkItemDtoAssembler>(),
            new WorkflowInstanceRepository(_db), new WorkflowVersionRepository(_db), new ActivityInstanceRepository(_db), groups.Object, Mock.Of<IWorkflowDepartmentRepository>(),
            Mock.Of<IWorkflowCandidateFactory>(), Mock.Of<IWorkItemCandidateRepository>(), forms.Object, roles.Object);
        var completion = await handler.Handle(new CompleteWorkItemCommand(item.Id, actor, _orgId, "APPROVE", FormValues: variants.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value)), FormVisitId: item.Id), default);
        completion.IsFailure.Should().BeTrue(); completion.Error.Code.Should().Be("Workflow.Form.Context");
        item.Status.Should().Be(WorkItemStatus.Claimed); forms.Verify(f => f.SubmitAsync(It.IsAny<FormSubmitRequest>(), default), Times.Once); engine.VerifyNoOtherCalls(); store.VerifyNoOtherCalls();
        (await guard.ValidateAsync(formId, 2, "WorkItem", item.Id + ":" + item.Id, item.FormReceiptKey(item.Id, actor), new Dictionary<string, object?> { [" A.B "] = 42 }, actor.ToString(), default)).IsSuccess.Should().BeTrue();
    }
    private async Task<(WorkflowMappingRuntime Runtime, WorkflowIntegrations Api, MappingSourceResolver Resolver,
        WorkflowMappingInvocation Invocation, WorkItem Item, SourceReference Source, Mock<IFormGateway> Forms,
        Mock<IWorkflowActorAccess> Access, Mock<IServiceProvider> Services)> IndependentFormSource(string address, DateTime anchor, bool wait = true)
    {
        var s = MappingServices(); var actor = Guid.NewGuid(); var connection = await Connection(s.Api, "Http", address, "None");
        var seed = await TypedSeed(connection); var form = Guid.NewGuid(); var type = Guid.NewGuid();
        var business = new BusinessActivityConfiguration { TaskTypeId = type, FormDefinitionId = form, FormVersionNo = 1 };
        _db.Add(ActivityDefinition.Create(seed.Version.Id, "submitted", ActivityType.UserTask, "Submitted", anchor.AddSeconds(-2), configurationJson: Config(business)));
        var root = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "form-source", "key", "activity", anchor.AddSeconds(-2), startedByUserId: actor);
        var submitted = ActivityInstance.Start(_orgId, root.Id, "submitted", ActivityType.UserTask, "Submitted", anchor.AddSeconds(-1));
        var origin = ActivityInstance.Start(_orgId, root.Id, "activity", ActivityType.ServiceTask, "Event", anchor);
        var item = WorkItem.Create(_orgId, root.Id, submitted.Id, Guid.NewGuid(), anchor); item.Claim(actor, anchor);
        var source = new SourceReference(seed.Version.WorkflowDefinitionId, seed.Version.Id, "submitted", "FormField", "text", FormId: form, FormVersion: 1);
        var mapping = EmptyMapping() with { Inputs = [new("body", "$body", new("object", Properties: new() { ["text"] = new("source", Source: source, Missing: wait ? new("Wait", 10, "expired") : null) }))] };
        var invocation = WorkflowMappingInvocation.Create(_orgId, root.Id, origin.Id, null, actor, root.Id, seed.Version.Id, "activity", "Callable",
            Config(new HttpActivityConfiguration { ConnectionId = connection, MappingMode = "Typed", TypedMapping = mapping }), "source-receipt", anchor);
        _db.AddRange(root, submitted, origin, item, invocation); await _db.SaveChangesAsync();
        var types = new Mock<IWorkflowTaskTypeCatalog>(); types.Setup(t => t.FormIdsAsync(type, default)).ReturnsAsync([form]);
        s.Forms.Setup(f => f.FindFieldActivityFormAsync(form, default)).ReturnsAsync(new FieldActivityFormInfo(form, "F", "Form", "Form", "", "Published", "D", "work", 1, [1], true, anchor));
        s.Forms.Setup(f => f.GetFieldsAsync(form, 1, default)).ReturnsAsync([new("text", "text", "Text", null, null, [])]);
        var resolver = new MappingSourceResolver(_db, s.Access.Object, s.Forms.Object, types.Object);
        return (new(_db, resolver, s.Services.Object), s.Api, resolver, invocation, item, source, s.Forms, s.Access, s.Services);
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(20, false)]
    public async Task IndependentReviewWaitUsesRecordedSubmissionTimeAcrossRestart(int availableSeconds, bool timely)
    {
        using var local = new MappingLocalServer(); var anchor = DateTime.UtcNow.AddMinutes(-1);
        var f = await IndependentFormSource(local.Address, anchor);
        await f.Runtime.PrepareAsync(f.Invocation, default); f.Invocation.Status.Should().Be("Waiting");
        var deadline = f.Invocation.Deadline;
        f.Item.Complete(f.Invocation.ActorUserId, "done", anchor.AddSeconds(availableSeconds));
        f.Forms.Setup(g => g.GetByClientIdAsync(f.Source.FormId!.Value, f.Item.Id, "WorkItem", f.Item.Id.ToString(), default))
            .ReturnsAsync(new FormSubmissionRecord(Guid.NewGuid(), f.Source.FormId!.Value, 1, null, null, anchor.AddSeconds(availableSeconds), new Dictionary<string, object?> { ["text"] = "recorded" }));
        await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();
        var runtime = new WorkflowMappingRuntime(_db, f.Resolver, f.Services.Object); await runtime.RecoverAsync(default);
        var invocation = await _db.MappingInvocations.SingleAsync(); invocation.WindowAnchor.Should().Be(anchor);
        invocation.Status.Should().Be(timely ? "Prepared" : "Attention");
        if (!timely) { invocation.Deadline.Should().Be(deadline); invocation.TimeoutOutcome.Should().Be("expired"); }
        else await new WorkflowIntegrationProcessor(_db, f.Api, new WorkflowIntegrationTransport(), Mock.Of<IWorkflowRuntimeEngine>(), mappings: runtime).ProcessJobAsync((await _db.IntegrationJobs.SingleAsync()).Id, default);
        local.Bodies.Should().HaveCount(timely ? 1 : 0);
    }

    [Fact]
    public async Task IndependentReviewSingleFormUsesCompletedClientReceiptAndNeverLatestContext()
    {
        using var local = new MappingLocalServer(); var f = await IndependentFormSource(local.Address, DateTime.UtcNow);
        var good = new FormSubmissionRecord(Guid.NewGuid(), f.Source.FormId!.Value, 1, null, null, DateTimeOffset.UtcNow, new Dictionary<string, object?> { ["text"] = "completed" });
        f.Forms.Setup(g => g.GetByClientIdAsync(f.Source.FormId.Value, f.Item.Id, "WorkItem", f.Item.Id.ToString(), default)).ReturnsAsync(good);
        f.Forms.Setup(g => g.GetLatestByContextAsync(f.Source.FormId.Value, "WorkItem", f.Item.Id.ToString(), default)).ReturnsAsync(good with { SubmissionId = Guid.NewGuid(), Answers = new Dictionary<string, object?> { ["text"] = "forged" } });
        var context = new MappingResolutionContext(_orgId, f.Invocation.ActorUserId, f.Invocation.RootInstanceId, f.Invocation.Id, DateTimeOffset.UtcNow, new Dictionary<string, FrozenRelatedSelection>());
        (await f.Resolver.ResolveAsync(f.Source, context, default)).State.Should().Be("Missing");
        f.Item.Complete(f.Invocation.ActorUserId, "done", DateTime.UtcNow); await _db.SaveChangesAsync();
        var resolved = await f.Resolver.ResolveAsync(f.Source, context, default);
        resolved.Value.GetString().Should().Be("completed"); resolved.SubmissionId.Should().Be(good.SubmissionId);
        f.Forms.Verify(g => g.GetLatestByContextAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Theory]
    [InlineData("Attention", "Attention")]
    [InlineData("Fallback", "Prepared")]
    [InlineData("Wait", "Attention")]
    public async Task IndependentReviewSelectorPolicyPrecedesCoalesce(string policy, string expected)
    {
        var s = MappingServices(); using var local = new MappingLocalServer(); var connection = await Connection(s.Api, "Http", local.Address, "None");
        var seed = await TypedSeed(connection); var anchor = DateTime.UtcNow.AddMinutes(-1); var actor = Guid.NewGuid();
        var missing = policy == "Fallback" ? new MissingDataPolicy("Fallback", Fallback: JsonSerializer.SerializeToElement<object?>(null))
            : policy == "Wait" ? new("Wait", 10, "expired") : new MissingDataPolicy();
        var mapping = EmptyMapping() with
        {
            Selectors = [new("previous", seed.Version.WorkflowDefinitionId, seed.Version.Id, "BusinessKey", Literal("key"), Missing: missing)],
            Inputs = [new("body", "$body", new("object", Properties: new() { ["text"] = new("coalesce", Args: [new("source", Source: new(seed.Version.WorkflowDefinitionId, seed.Version.Id, "$input", "WorkflowInput", "value", SelectorId: "previous")), Literal("fallback")]) }))]
        };
        var root = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "selector-root", "key", "activity", anchor, startedByUserId: actor);
        var origin = ActivityInstance.Start(_orgId, root.Id, "activity", ActivityType.ServiceTask, "Event", anchor);
        var invocation = WorkflowMappingInvocation.Create(_orgId, root.Id, origin.Id, null, actor, root.Id, seed.Version.Id, "activity", "Callable", Config(new HttpActivityConfiguration { ConnectionId = connection, MappingMode = "Typed", TypedMapping = mapping }), "selector", anchor);
        _db.AddRange(root, origin, invocation); await _db.SaveChangesAsync(); await s.Runtime.RecoverAsync(default);
        invocation.Status.Should().Be(expected); invocation.SelectionsJson.Should().NotContain(root.Id.ToString());
        if (expected == "Prepared") invocation.PreparedRequestJson.Should().Contain("fallback");
        (await _db.IntegrationJobs.CountAsync()).Should().Be(expected == "Prepared" ? 1 : 0);
    }

    [Theory]
    [InlineData("Latest")]
    [InlineData("Attention")]
    public async Task IndependentReviewPreviousSelectorAllowsPriorSameVersionButExcludesOwner(string mode)
    {
        var s = MappingServices(); var seed = await TypedSeed(Guid.NewGuid()); var now = DateTime.UtcNow; var actor = Guid.NewGuid();
        var previous = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "previous", "customer", "activity", now.AddMinutes(-1));
        var owner = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "owner", "customer", "activity", now);
        _db.AddRange(previous, owner); await _db.SaveChangesAsync();
        var criteria = new FrozenRelatedCriteria("previous", seed.Version.WorkflowDefinitionId, seed.Version.Id, "BusinessKey", "customer", null, null, now, now.AddHours(-1), null, mode, "timeout");
        (await s.Resolver.SelectRelatedAsync(criteria, _orgId, actor, default, owner.Id))!.Id.Should().Be(previous.Id);
    }

    [Fact]
    public async Task IndependentReviewEmptyTypedStepIsSafeNoOpAndLegacySubmissionRemainsUsable()
    {
        var s = MappingServices(); var actor = Guid.NewGuid(); var form = Guid.NewGuid();
        var config = new BusinessActivityConfiguration { SlaDurationHours = 1, FormDefinitionId = form, FormVersionNo = 1, TypedMapping = new(1, null, [], [], [], [], []) };
        var seed = SeedSingleActivity(ActivityType.UserTask, Config(config), DateTime.UtcNow); RoutingAssignments();
        var version = (await _db.WorkflowVersions.FindAsync(seed.Version))!; version.SetWorkspace(Config(new WorkflowWorkspaceDefinition("Main", "C", "B", DesignerVersion: 4))); await _db.SaveChangesAsync();
        var started = await BuildEngine(_db, s.Api, stepMappings: s.Runtime).StartAsync(_orgId, seed.Binding.Id, "order", "empty", DateTime.UtcNow, startedByUserId: actor, pinnedWorkflowVersionId: seed.Version);
        started.IsSuccess.Should().BeTrue(); var item = await _db.WorkItems.SingleAsync(); item.Status.Should().Be(WorkItemStatus.Pending);
        (await _db.MappingInvocations.CountAsync()).Should().Be(0);
        item.Claim(actor, DateTime.UtcNow); await _db.SaveChangesAsync();
        (await new WorkflowFormSubmissionGuard(_db).ValidateAsync(form, 1, "WorkItem", item.Id.ToString(), item.FormReceiptKey(item.Id, actor), new Dictionary<string, object?>(), actor.ToString(), default)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task IndependentReviewCancelledCallAndOldVisitCannotCancelPreparingStep()
    {
        var s = MappingServices(); var seed = await TypedSeed(Guid.NewGuid()); var now = DateTime.UtcNow; var actor = Guid.NewGuid();
        var root = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "preparing", "key", "activity", now, startedByUserId: actor);
        var activity = ActivityInstance.Start(_orgId, root.Id, "activity", ActivityType.UserTask, "Step", now);
        var item = WorkItem.Create(_orgId, root.Id, activity.Id, Guid.NewGuid(), now); item.WaitForMapping();
        var visit = Guid.NewGuid(); item.SetFormProgress(Config(new ActivityFormProgress { VisitId = visit, ActiveStepId = "form" }));
        WorkflowMappingInvocation Cancelled(string kind, Guid? formVisit) { var receipt = WorkflowMappingInvocation.Create(_orgId, root.Id, activity.Id, null, actor, root.Id, seed.Version.Id, "activity", kind, "{}", Guid.NewGuid().ToString(), now, formVisitId: formVisit); receipt.Cancel(); return receipt; }
        _db.AddRange(root, activity, item, Cancelled("Callable", visit), Cancelled("Step", Guid.NewGuid())); await _db.SaveChangesAsync();
        await s.Runtime.RecoverAsync(default); item.Status.Should().Be(WorkItemStatus.Preparing);
        _db.Add(Cancelled("Step", visit)); await _db.SaveChangesAsync(); await s.Runtime.RecoverAsync(default); item.Status.Should().Be(WorkItemStatus.Cancelled);
    }

    [Fact]
    public async Task IndependentReviewPoisonRecoveryDoesNotStarveLaterInvocation()
    {
        var s = MappingServices(); using var local = new MappingLocalServer(); var connection = await Connection(s.Api, "Http", local.Address, "None"); var seed = await TypedSeed(connection); var now = DateTime.UtcNow;
        var root = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "recovery", "key", "activity", now, startedByUserId: Guid.NewGuid());
        var activity = ActivityInstance.Start(_orgId, root.Id, "activity", ActivityType.ServiceTask, "Event", now);
        var poison = WorkflowMappingInvocation.Create(_orgId, root.Id, activity.Id, null, root.StartedByUserId!.Value, root.Id, seed.Version.Id, "activity", "Graph", seed.Node.ConfigurationJson!, "poison", now.AddMinutes(-1)); poison.Complete("{}", now);
        var healthy = WorkflowMappingInvocation.Create(_orgId, root.Id, activity.Id, null, root.StartedByUserId.Value, root.Id, seed.Version.Id, "activity", "Callable", seed.Node.ConfigurationJson!, "healthy", now);
        _db.AddRange(root, activity, poison, healthy); await _db.SaveChangesAsync();
        var engine = new Mock<IWorkflowRuntimeEngine>(); engine.Setup(e => e.CompleteExternalActivityAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyDictionary<string, object?>>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTime>(), default)).ThrowsAsync(new InvalidOperationException("poison graph"));
        s.Services.Setup(p => p.GetService(typeof(IWorkflowRuntimeEngine))).Returns(engine.Object);
        await s.Runtime.RecoverAsync(default); await s.Runtime.RecoverAsync(default);
        (await _db.MappingInvocations.SingleAsync(i => i.Id == poison.Id)).Error.Should().Contain("poison graph");
        (await _db.MappingInvocations.SingleAsync(i => i.Id == healthy.Id)).Status.Should().Be("Prepared");
        await new WorkflowIntegrationProcessor(_db, s.Api, new WorkflowIntegrationTransport(), engine.Object, mappings: s.Runtime).ProcessJobAsync((await _db.IntegrationJobs.SingleAsync()).Id, default); local.Bodies.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task IndependentReviewFollowUpRetainsActualActionAndAncestorOutputAuthorization(bool revoke, bool viaOutput, bool beforeQueue)
    {
        using var local = new MappingLocalServer(); var f = await IndependentFormSource(local.Address, DateTime.UtcNow, false);
        var parent = f.Invocation; var http = IntegrationJson.Read<HttpActivityConfiguration>(parent.ConfigurationJson);
        var version = (await _db.WorkflowVersions.FindAsync(parent.TargetVersionId))!;
        var output = new SourceReference(version.WorkflowDefinitionId, version.Id, "activity", "EventOutput", "private-output");
        var mapping = EmptyMapping() with
        {
            Outputs = viaOutput ? [new("private-output", "Private", new("string"), new("source", Source: f.Source))] : [],
            Actions = [new("call", new(version.WorkflowDefinitionId, version.Id, "call"), "Run", new("source", Source: viaOutput ? output : f.Source))]
        };
        http.TypedMapping = mapping; _db.Entry(parent).Property(p => p.ConfigurationJson).CurrentValue = Config(http);
        var childMapping = EmptyMapping() with { Callable = true, InvocationInputs = new("string"), Inputs = [new("body", "$body", new("object", Properties: new() { ["text"] = new("source", Source: new(version.WorkflowDefinitionId, version.Id, "call", "InvocationInput", "$inputs")) }))] };
        _db.Add(ActivityDefinition.Create(version.Id, "call", ActivityType.ServiceTask, "Call", DateTime.UtcNow, actionKey: "http.request", configurationJson: Config(new HttpActivityConfiguration { ConnectionId = http.ConnectionId, MappingMode = "Typed", TypedMapping = childMapping })));
        f.Item.Complete(parent.ActorUserId, "done", DateTime.UtcNow);
        f.Forms.Setup(g => g.GetByClientIdAsync(f.Source.FormId!.Value, f.Item.Id, "WorkItem", f.Item.Id.ToString(), default))
            .ReturnsAsync(new FormSubmissionRecord(Guid.NewGuid(), f.Source.FormId!.Value, 1, null, null, DateTimeOffset.UtcNow, new Dictionary<string, object?> { ["text"] = "private action value" }));
        await _db.SaveChangesAsync(); await f.Runtime.PrepareAsync(parent, default);
        var processor = new WorkflowIntegrationProcessor(_db, f.Api, new WorkflowIntegrationTransport(), Mock.Of<IWorkflowRuntimeEngine>(), mappings: f.Runtime);
        await processor.ProcessJobAsync((await _db.IntegrationJobs.SingleAsync()).Id, default); parent.Applied.Should().BeTrue();
        var child = await _db.MappingInvocations.SingleAsync(i => i.ParentInvocationId == parent.Id); child.SourcesJson.Should().Contain("FormField");
        if (!beforeQueue) await f.Runtime.RecoverAsync(default);
        if (revoke) f.Access.Setup(a => a.GetAsync(parent.ActorUserId, default)).ReturnsAsync(new WorkflowActorAccess([NwfmPolicies.ViewInstances], OrgScopeSet.Unrestricted(), OrgHierarchy.Empty));
        if (beforeQueue) await f.Runtime.RecoverAsync(default);
        var job = await _db.IntegrationJobs.SingleOrDefaultAsync(j => j.InvocationId == child.Id);
        if (job is not null) await processor.ProcessJobAsync(job.Id, default);
        child.Status.Should().Be(revoke ? "Attention" : "Completed"); local.Bodies.Should().HaveCount(revoke ? 1 : 2);
        if (!revoke) local.Bodies.Last().Should().Contain("private action value");
    }

    [Fact]
    public async Task IndependentReviewResponseSchemaFailureTakesRealGraphErrorRouteOnceWithoutResend()
    {
        var s = MappingServices(); using var local = new MappingLocalServer(); var connection = await Connection(s.Api, "Http", local.Address, "None");
        var mapping = EmptyMapping() with { Endpoint = EmptyMapping().Endpoint! with { Response = new("string") } }; var seed = await TypedSeed(connection, mapping);
        var engine = BuildEngine(_db, s.Api); s.Services.Setup(p => p.GetService(typeof(IWorkflowRuntimeEngine))).Returns(engine);
        var started = await engine.StartAsync(_orgId, seed.Binding.Id, "order", "failure", DateTime.UtcNow, startedByUserId: Guid.NewGuid(), pinnedWorkflowVersionId: seed.Version.Id);
        started.IsSuccess.Should().BeTrue(); var job = await _db.IntegrationJobs.SingleAsync();
        var processor = new WorkflowIntegrationProcessor(_db, s.Api, new WorkflowIntegrationTransport(), engine, mappings: s.Runtime);
        await processor.ProcessJobAsync(job.Id, default);
        var invocation = await _db.MappingInvocations.SingleAsync(); invocation.GraphAdvanced.Should().BeTrue(); invocation.Status.Should().Be("Attention");
        (await _db.ActivityInstances.FindAsync(job.ActivityInstanceId))!.Status.Should().NotBe(ActivityInstanceStatus.Active);
        (await s.Api.ReplayOperationAsync(job.Id, default)).IsFailure.Should().BeTrue();
        await processor.ProcessJobAsync(job.Id, default); await s.Runtime.RecoverAsync(default);
        (await _db.RoutingResults.CountAsync()).Should().Be(1); local.Bodies.Should().ContainSingle();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IndependentReviewTypedAttachedFailureQueuesOnFailureExactlyOnce(bool transportFailure)
    {
        var s = MappingServices(); using var local = new MappingLocalServer(transportFailure ? 500 : 200, 200);
        var connection = await Connection(s.Api, "Http", local.Address, "None"); var now = DateTime.UtcNow;
        var seed = SeedSingleActivity(ActivityType.Timer, "{\"duration\":\"01:00:00\"}", now);
        var version = (await _db.WorkflowVersions.FindAsync(seed.Version))!; version.SetWorkspace(Config(new WorkflowWorkspaceDefinition("Main", "C", "B", DesignerVersion: 4)));
        var mapping = transportFailure ? EmptyMapping() : EmptyMapping() with { Endpoint = EmptyMapping().Endpoint! with { Response = new("string") } };
        _db.AddRange(ActivityDefinition.Create(seed.Version, "attached", ActivityType.ServiceTask, "Attached", now, actionKey: "http.request", configurationJson: Config(new
        { connectionId = connection, mappingMode = "Typed", typedMapping = mapping, maxAttempts = 1, required = false, triggerBinding = new { sourceNodeKey = "activity", trigger = "OnComment" } })),
            ActivityDefinition.Create(seed.Version, "failed", ActivityType.ServiceTask, "Failure handler", now, actionKey: "http.request", configurationJson: Config(new
            { connectionId = connection, maxAttempts = 1, required = false, triggerBinding = new { sourceNodeKey = "activity", trigger = "OnFailure" } })));
        await _db.SaveChangesAsync(); var engine = BuildEngine(_db, s.Api, stepMappings: s.Runtime);
        var root = (await engine.StartAsync(_orgId, seed.Binding.Id, "order", "attached", now, startedByUserId: Guid.NewGuid(), pinnedWorkflowVersionId: seed.Version)).Value;
        var origin = await _db.ActivityInstances.SingleAsync(a => a.ActivityNodeKey == "activity"); var definition = await _db.ActivityDefinitions.SingleAsync(a => a.WorkflowVersionId == seed.Version && a.NodeKey == "activity");
        var events = new WorkflowActivityEvents(_db, s.Api, s.Runtime); await events.QueueAsync(root, definition, origin, "OnComment", "one", default);
        var processor = new WorkflowIntegrationProcessor(_db, s.Api, new WorkflowIntegrationTransport(), engine, events, s.Runtime);
        var job = await _db.IntegrationJobs.SingleAsync(); await processor.ProcessJobAsync(job.Id, default);
        var failure = await _db.IntegrationJobs.SingleAsync(j => j.EventTrigger == "OnFailure");
        await processor.ProcessJobAsync(job.Id, default); await processor.ProcessJobAsync(failure.Id, default);
        (await _db.IntegrationJobs.CountAsync(j => j.EventTrigger == "OnFailure")).Should().Be(1);
        origin.Status.Should().Be(ActivityInstanceStatus.Active); (await _db.RoutingResults.CountAsync()).Should().Be(0); local.Bodies.Should().HaveCount(2);
    }
}
