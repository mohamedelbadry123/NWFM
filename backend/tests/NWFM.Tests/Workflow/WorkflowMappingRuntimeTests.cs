namespace NWFM.Tests.Modules.Workflow;

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.Integrations;
using global::Workflow.Application.Mapping;
using global::Workflow.Application.Settings;
using global::Workflow.Application.Workspace;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using NWFM.Shared.Constants;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Organization;
using NWFM.Shared.Results;

public sealed partial class WorkflowRuntimeEnginePathTests
{
    private static TransformNode Literal(object? value) => new("literal", JsonSerializer.SerializeToElement(value));
    private static DesignerMappingConfiguration EmptyMapping() => new(1, new("POST", "/typed", [], new("object", Properties: new() { ["text"] = new("string", Required: true) }), new("object", Properties: new() { ["result"] = new("number", Required: true) })),
        [new("body", "$body", new("object", Properties: new() { ["text"] = Literal("{{literal}}") }))], [], [], [], []);
    private (WorkflowMappingRuntime Runtime, WorkflowIntegrations Api, MappingSourceResolver Resolver, Mock<IWorkflowActorAccess> Access, Mock<IFormGateway> Forms, Mock<IServiceProvider> Services) MappingServices()
    {
        var access = new Mock<IWorkflowActorAccess>();
        access.Setup(a => a.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new WorkflowActorAccess([NwfmPolicies.ManageDefinitions, NwfmPolicies.ViewInstances, NwfmPolicies.ViewSubmissions], OrgScopeSet.Unrestricted(), OrgHierarchy.Build([], [], [])));
        var forms = new Mock<IFormGateway>(); var resolver = new MappingSourceResolver(_db, access.Object, forms.Object);
        var services = new Mock<IServiceProvider>();
        var runtime = new WorkflowMappingRuntime(_db, resolver, services.Object);
        var api = new WorkflowIntegrations(_db, new StubTenant(_orgId), new EphemeralDataProtectionProvider(), new WorkflowIntegrationTransport(), Options.Create(new WorkflowSettings { AllowPrivateConnections = true }), runtime);
        return (runtime, api, resolver, access, forms, services);
    }
    private async Task<(WorkflowBinding Binding, WorkflowVersion Version, ActivityDefinition Node)> TypedSeed(Guid connection, DesignerMappingConfiguration? mapping = null)
    {
        var seed = SeedSingleActivity(ActivityType.ServiceTask, Config(new HttpActivityConfiguration { ConnectionId = connection, MappingMode = "Typed", TypedMapping = mapping ?? EmptyMapping() }), DateTime.UtcNow, "http.request");
        var version = (await _db.WorkflowVersions.FindAsync(seed.Version))!;
        version.SetWorkspace(Config(new WorkflowWorkspaceDefinition("Main", "C", "B", DesignerVersion: 4)));
        await _db.SaveChangesAsync();
        return (seed.Binding, version, await _db.ActivityDefinitions.SingleAsync(a => a.WorkflowVersionId == version.Id && a.NodeKey == "activity"));
    }

    [Fact]
    public async Task TypedGraphAndCallableUseIndependentReceiptsAndLiteralPreparedPayloads()
    {
        var s = MappingServices(); using var local = new MappingLocalServer();
        var connection = await Connection(s.Api, "Http", local.Address, "None");
        var seed = await TypedSeed(connection);
        var response = new SourceReference(seed.Version.WorkflowDefinitionId, seed.Version.Id, "activity", "Response", "$response", "/result");
        var mapping = EmptyMapping() with
        {
            Outputs = [new("total", "Total", new("number"), new("source", Source: response))],
            Actions = [new("stable-action", new(seed.Version.WorkflowDefinitionId, seed.Version.Id, "callable"), "Run", new("source", Source: response))]
        };
        seed.Node.SetPublishedConfiguration(Config(new HttpActivityConfiguration { ConnectionId = connection, MappingMode = "Typed", TypedMapping = mapping }));
        var callable = EmptyMapping() with { Callable = true, InvocationInputs = new("number"), Inputs = [new("body", "$body", new("object", Properties: new() { ["text"] = new("text", Args: [new("source", Source: new(seed.Version.WorkflowDefinitionId, seed.Version.Id, "activity", "EventOutput", "total"))]) }))] };
        _db.ActivityDefinitions.Add(ActivityDefinition.Create(seed.Version.Id, "callable", ActivityType.ServiceTask, "Callable", DateTime.UtcNow, actionKey: "http.request", configurationJson: Config(new HttpActivityConfiguration { ConnectionId = connection, MappingMode = "Typed", TypedMapping = callable })));
        await _db.SaveChangesAsync();
        var actor = Guid.NewGuid();
        var catalogs = new DesignerMappingCatalog(_db, new global::Workflow.Infrastructure.Persistence.Repositories.WorkflowVersionRepository(_db), new StubTenant(_orgId),
            Mock.Of<NWFM.Shared.Abstractions.ICurrentUser>(u => u.Id == actor.ToString()), s.Access.Object, s.Forms.Object, Mock.Of<IWorkflowTaskTypeCatalog>());
        (await new MappingPublicationValidator(catalogs).ValidateAsync(seed.Version, default)).Should().BeEmpty();
        var engine = BuildEngine(_db, s.Api); s.Services.Setup(p => p.GetService(typeof(IWorkflowRuntimeEngine))).Returns(engine);
        var started = await engine.StartAsync(_orgId, seed.Binding.Id, "order", "mapped", DateTime.UtcNow, startedByUserId: Guid.NewGuid(), pinnedWorkflowVersionId: seed.Version.Id);
        started.IsSuccess.Should().BeTrue(started.IsFailure ? started.Error.Message : "");
        var job = await _db.IntegrationJobs.SingleAsync(); job.ExecutionKind.Should().Be("Graph");
        var transport = new WorkflowIntegrationTransport(); var processor = new WorkflowIntegrationProcessor(_db, s.Api, transport, engine, mappings: s.Runtime);
        await processor.ProcessJobAsync(job.Id, default);
        (await _db.MappingInvocations.SingleAsync(i => i.ExecutionKind == "Graph")).GraphAdvanced.Should().BeTrue();
        (await s.Api.ReplayOperationAsync(job.Id, default)).IsFailure.Should().BeTrue();
        await processor.ProcessJobAsync(job.Id, default);
        var graphResult = await _db.RoutingResults.SingleAsync(); graphResult.ValuesJson.Should().Contain("total");
        var graphActivity = await _db.ActivityInstances.FindAsync(job.ActivityInstanceId); graphActivity!.Status.Should().Be(ActivityInstanceStatus.Completed);
        var count = await _db.ActivityInstances.CountAsync(); var tokenCount = await _db.WorkflowExecutionTokens.CountAsync();
        await s.Runtime.RecoverAsync(default);
        var callJob = await _db.IntegrationJobs.SingleAsync(j => j.ExecutionKind == "Callable");
        await processor.ProcessJobAsync(callJob.Id, default); await processor.ProcessJobAsync(callJob.Id, default);
        (await _db.RoutingResults.CountAsync()).Should().Be(1); (await _db.ActivityInstances.CountAsync()).Should().Be(count);
        (await _db.WorkflowExecutionTokens.CountAsync()).Should().Be(tokenCount);
        (await _db.MappingInvocations.SingleAsync(i => i.ExecutionKind == "Callable")).Applied.Should().BeTrue();
        local.Bodies.Should().Equal("{\"text\":\"{{literal}}\"}", "{\"text\":\"7\"}");
    }

    [Fact]
    public async Task PreparedTestHasNoWorkflowWritesAndRejectsOriginAndHeaderInjection()
    {
        var s = MappingServices(); using var local = new MappingLocalServer();
        var connection = await Connection(s.Api, "Http", local.Address, "None");
        var c = new HttpActivityConfiguration { ConnectionId = connection, MappingMode = "Typed", TypedMapping = EmptyMapping() };
        var prepared = MappingRequestPreparation.Prepare(c.TypedMapping, []);
        (await s.Api.TestPreparedHttpAsync(c, prepared, default)).IsSuccess.Should().BeTrue();
        (await _db.MappingInvocations.CountAsync()).Should().Be(0); (await _db.WorkflowInstances.CountAsync()).Should().Be(0); (await _db.IntegrationJobs.CountAsync()).Should().Be(0);
        (await s.Api.TestPreparedHttpAsync(c, prepared with { Path = "//example.com" }, default)).IsFailure.Should().BeTrue();
        (await s.Api.TestPreparedHttpAsync(c, prepared with { Headers = new() { ["Host"] = "example.com" } }, default)).IsFailure.Should().BeTrue();
        local.Bodies.Should().ContainSingle().Which.Should().Contain("{{literal}}");
    }

    [Fact]
    public async Task DeliveredOutputApplicationRetryDoesNotResendOrReopenGraph()
    {
        var s = MappingServices(); using var local = new MappingLocalServer();
        var connection = await Connection(s.Api, "Http", local.Address, "None"); var seed = await TypedSeed(connection);
        var engine = BuildEngine(_db, s.Api);
        (await engine.StartAsync(_orgId, seed.Binding.Id, "order", "recover", DateTime.UtcNow, startedByUserId: Guid.NewGuid(), pinnedWorkflowVersionId: seed.Version.Id)).IsSuccess.Should().BeTrue();
        var completion = new Mock<IWorkflowRuntimeEngine>(); var calls = 0;
        completion.Setup(e => e.CompleteExternalActivityAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyDictionary<string, object?>>(), "success", null, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++calls == 1 ? Result.Failure(new Error("temporary", "retry application")) : Result.Success());
        var processor = new WorkflowIntegrationProcessor(_db, s.Api, new WorkflowIntegrationTransport(), completion.Object, mappings: s.Runtime);
        var job = await _db.IntegrationJobs.SingleAsync(); await processor.ProcessJobAsync(job.Id, default);
        job.Status.Should().Be("Failed"); job.DeliveryResultJson.Should().NotBeNull();
        (await s.Api.ReplayOperationAsync(job.Id, default)).IsSuccess.Should().BeTrue(); job.Status.Should().Be("Delivered");
        await processor.ProcessJobAsync(job.Id, default);
        local.Bodies.Should().HaveCount(1); calls.Should().Be(2); job.Status.Should().Be("Completed");
        (await _db.MappingInvocations.SingleAsync()).GraphAdvanced.Should().BeTrue();
    }

    [Theory]
    [InlineData("MapOnly")]
    [InlineData("Run")]
    public async Task MissingChildNeverCreatesAnInstanceOrDispatch(string mode)
    {
        var s = MappingServices(); var seed = await TypedSeed(Guid.NewGuid());
        var root = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "root", "order", "activity", DateTime.UtcNow, startedByUserId: Guid.NewGuid());
        var origin = ActivityInstance.Start(_orgId, root.Id, "activity", ActivityType.ServiceTask, "Activity", DateTime.UtcNow);
        _db.AddRange(root, origin); await _db.SaveChangesAsync();
        var missingVersion = Guid.NewGuid();
        var parentMapping = EmptyMapping() with { Actions = [new("missing-child", new(Guid.NewGuid(), missingVersion, "target"), mode, Literal(1), new("Wait", 300, "target-timeout"))] };
        var parent = WorkflowMappingInvocation.Create(_orgId, root.Id, origin.Id, null, root.StartedByUserId!.Value, root.Id, seed.Version.Id, "activity", "Graph",
            Config(new HttpActivityConfiguration { MappingMode = "Typed", TypedMapping = parentMapping }), "parent", DateTime.UtcNow);
        parent.Complete("{}", DateTime.UtcNow); parent.MarkGraphAdvanced(); _db.Add(parent);
        var invocation = WorkflowMappingInvocation.Create(_orgId, root.Id, origin.Id, null, root.StartedByUserId!.Value, Guid.Empty, missingVersion,
            "target", mode == "MapOnly" ? "MapOnly" : "Callable", seed.Node.ConfigurationJson!, "missing", DateTime.UtcNow, parent.Id, "missing-child");
        invocation.Wait(DateTime.UtcNow.AddMinutes(5), "target absent", "timeout"); _db.Add(invocation); await _db.SaveChangesAsync();
        await s.Runtime.RecoverAsync(default);
        invocation.Status.Should().Be("Waiting"); (await _db.WorkflowInstances.CountAsync()).Should().Be(1); (await _db.IntegrationJobs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RelatedSelectionUsesExactPinsKeysWindowStableLatestAndExplicitActor()
    {
        var s = MappingServices(); var seed = await TypedSeed(Guid.NewGuid()); var actor = Guid.NewGuid(); var anchor = DateTimeOffset.UtcNow;
        WorkflowInstance Instance(string key, DateTime at) => WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, Guid.NewGuid().ToString(), key, "activity", at, startedByUserId: actor);
        var first = Instance("key", anchor.UtcDateTime.AddMinutes(-2)); var last = Instance("key", anchor.UtcDateTime.AddMinutes(-1)); var future = Instance("key", anchor.UtcDateTime.AddMinutes(1));
        _db.AddRange(first, last, future, Instance("other", anchor.UtcDateTime)); await _db.SaveChangesAsync();
        var criteria = new FrozenRelatedCriteria("related", seed.Version.WorkflowDefinitionId, seed.Version.Id, "BusinessKey", "key", actor, "Running", anchor, anchor.AddHours(-1), anchor.AddMinutes(5), "Attention", "timeout");
        Func<Task> ambiguous = async () => await s.Resolver.SelectRelatedAsync(criteria, _orgId, actor, default); await ambiguous.Should().ThrowAsync<FormatException>();
        (await s.Resolver.SelectRelatedAsync(criteria with { MultipleMatches = "Latest" }, _orgId, actor, default))!.Id.Should().Be(last.Id);
        (await s.Resolver.SelectRelatedAsync(criteria with { KeyValue = "absent" }, _orgId, actor, default)).Should().BeNull();
        Func<Task> crossTenant = async () => await s.Resolver.SelectRelatedAsync(criteria with { MultipleMatches = "Latest" }, Guid.NewGuid(), actor, default);
        await crossTenant.Should().ThrowAsync<UnauthorizedAccessException>();
        s.Access.Setup(a => a.GetAsync(actor, It.IsAny<CancellationToken>())).ReturnsAsync((WorkflowActorAccess?)null);
        Func<Task> forbidden = async () => await s.Resolver.SelectRelatedAsync(criteria with { MultipleMatches = "Latest" }, _orgId, actor, default);
        await forbidden.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task WaitRecoversAfterRegistrationBindsOnceAndKeepsPreparedPayloadFrozen()
    {
        var s = MappingServices(); using var local = new MappingLocalServer();
        var connection = await Connection(s.Api, "Http", local.Address, "None"); var seed = await TypedSeed(connection);
        var relatedDefinition = WorkflowDefinition.Create(_orgId, "related", "Related", DateTime.UtcNow);
        var relatedVersion = WorkflowVersion.CreateDraft(relatedDefinition.Id, 1, Guid.NewGuid(), DateTime.UtcNow);
        relatedVersion.SetWorkspace(Config(new WorkflowWorkspaceDefinition("Main", "C", "B", DesignerVersion: 4))); relatedVersion.Publish(Guid.NewGuid(), DateTime.UtcNow);
        _db.AddRange(relatedDefinition, relatedVersion);
        var source = new SourceReference(relatedDefinition.Id, relatedVersion.Id, "$input", "WorkflowInput", "name", SelectorId: "related");
        var mapping = EmptyMapping() with
        {
            Selectors = [new("related", relatedDefinition.Id, relatedVersion.Id, "BusinessKey", Literal("customer"), MultipleMatches: "Latest", Missing: new("Wait", 300, "timeout"))],
            Inputs = [new("body", "$body", new("object", Properties: new() { ["text"] = new("source", Source: source, Missing: new("Wait", 300, "timeout")) }))]
        };
        seed.Node.SetPublishedConfiguration(Config(new HttpActivityConfiguration { ConnectionId = connection, MappingMode = "Typed", TypedMapping = mapping })); await _db.SaveChangesAsync();
        var engine = BuildEngine(_db, s.Api); s.Services.Setup(p => p.GetService(typeof(IWorkflowRuntimeEngine))).Returns(engine);
        (await engine.StartAsync(_orgId, seed.Binding.Id, "order", "wait", DateTime.UtcNow, startedByUserId: Guid.NewGuid(), pinnedWorkflowVersionId: seed.Version.Id)).IsSuccess.Should().BeTrue();
        var invocation = await _db.MappingInvocations.SingleAsync(); invocation.Status.Should().Be("Waiting"); var anchor = invocation.WindowAnchor;
        (await _db.IntegrationJobs.CountAsync()).Should().Be(0);
        var related = WorkflowInstance.Start(_orgId, Guid.NewGuid(), relatedVersion.Id, "related-first", "customer", "start", DateTime.UtcNow);
        _db.Add(related); await _db.SaveChangesAsync();
        await s.Runtime.RecoverAsync(default); invocation.Status.Should().Be("Waiting"); invocation.SelectionsJson.Should().Contain(related.Id.ToString());
        var later = WorkflowInstance.Start(_orgId, Guid.NewGuid(), relatedVersion.Id, "related-later", "customer", "start", DateTime.UtcNow.AddSeconds(1));
        _db.AddRange(later, new WorkflowRoutingScope { Id = related.Id, OrganizationId = _orgId, WorkflowInstanceId = related.Id, ValuesJson = "{\"$input.name\":\"original\"}" });
        await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();
        var restarted = MappingServices(); restarted.Services.Setup(p => p.GetService(typeof(IWorkflowRuntimeEngine))).Returns(engine);
        await restarted.Runtime.RecoverAsync(default);
        invocation = await _db.MappingInvocations.SingleAsync(); invocation.Status.Should().Be("Prepared"); invocation.WindowAnchor.Should().Be(anchor);
        invocation.SelectionsJson.Should().Contain(related.Id.ToString()).And.NotContain(later.Id.ToString());
        var frozen = invocation.PreparedRequestJson;
        (await _db.RoutingScopes.SingleAsync(x => x.Id == related.Id)).ValuesJson = "{\"$input.name\":\"changed\"}"; await _db.SaveChangesAsync();
        await restarted.Runtime.PrepareAsync(invocation, default); invocation.PreparedRequestJson.Should().Be(frozen);
        var job = await _db.IntegrationJobs.SingleAsync();
        await new WorkflowIntegrationProcessor(_db, s.Api, new WorkflowIntegrationTransport(), engine, mappings: restarted.Runtime).ProcessJobAsync(job.Id, default);
        local.Bodies.Should().ContainSingle().Which.Should().Contain("original").And.NotContain("changed");
    }

    [Theory]
    [InlineData("Cancelled")]
    [InlineData("Suspended")]
    public async Task WaitingInvocationHonorsRootLifecycleWithoutJobs(string lifecycle)
    {
        var s = MappingServices(); var seed = await TypedSeed(Guid.NewGuid());
        var root = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "root", "order", "activity", DateTime.UtcNow, startedByUserId: Guid.NewGuid());
        var origin = ActivityInstance.Start(_orgId, root.Id, "activity", ActivityType.ServiceTask, "Activity", DateTime.UtcNow);
        var invocation = WorkflowMappingInvocation.Create(_orgId, root.Id, origin.Id, null, root.StartedByUserId!.Value, Guid.Empty, Guid.NewGuid(), "target", "Callable", seed.Node.ConfigurationJson!, "wait", DateTime.UtcNow);
        invocation.Wait(DateTime.UtcNow.AddSeconds(-1), "waiting", "timeout");
        if (lifecycle == "Cancelled") root.Cancel(DateTime.UtcNow); else root.Suspend(DateTime.UtcNow);
        _db.AddRange(root, origin, invocation); await _db.SaveChangesAsync();
        await s.Runtime.RecoverAsync(default);
        invocation.Status.Should().Be(lifecycle == "Cancelled" ? "Cancelled" : "Waiting"); (await _db.IntegrationJobs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task TypedStepSnapshotsAreStablePerVisitAndGenericSubmissionRejectsTampering()
    {
        var s = MappingServices(); var formId = Guid.NewGuid(); var actor = Guid.NewGuid();
        var config = new BusinessActivityConfiguration
        {
            SlaDurationHours = 1,
            FormSteps = [new() { Id = "first", FormId = formId, VersionNo = 2, Name = "Form" }],
            TypedMapping = new(1, null, [], [], [], [new("readonly", "first", formId, 2, "a.b", "ReadOnly", new("number"), Literal(42)), new("prefill", "first", formId, 2, "editable", "Prefill", new("string"), Literal("initial"))], [])
        };
        var seed = SeedSingleActivity(ActivityType.UserTask, Config(config), DateTime.UtcNow);
        var version = (await _db.WorkflowVersions.FindAsync(seed.Version))!; version.SetWorkspace(Config(new WorkflowWorkspaceDefinition("Main", "C", "B", DesignerVersion: 4)));
        await _db.SaveChangesAsync(); RoutingAssignments();
        var engine = BuildEngine(_db, s.Api, stepMappings: s.Runtime);
        var started = await engine.StartAsync(_orgId, seed.Binding.Id, "order", "step-root", DateTime.UtcNow, startedByUserId: actor, pinnedWorkflowVersionId: version.Id);
        started.IsSuccess.Should().BeTrue(started.IsFailure ? started.Error.Message : "");
        var item = await _db.WorkItems.SingleAsync(); item.Status.Should().Be(WorkItemStatus.Pending);
        item.Claim(actor, DateTime.UtcNow); await _db.SaveChangesAsync();
        var snapshot = await s.Runtime.ReadStepAsync(item, item.Id, default); snapshot!.Status.Should().Be("Completed"); snapshot.Values.Should().HaveCount(2);
        config.TypedMapping.StepBindings[0] = config.TypedMapping.StepBindings[0] with { Value = Literal(99) };
        await s.Runtime.PrepareStepAsync(item, Config(config), default);
        (await s.Runtime.ReadStepAsync(item, item.Id, default))!.Values.Single(v => v.FieldKey == "a.b").Value.GetInt32().Should().Be(42);
        var guard = new WorkflowFormSubmissionGuard(_db);
        var answers = new Dictionary<string, object?> { ["a.b"] = 42, ["editable"] = "user edit" };
        (await guard.ValidateAsync(formId, 2, "WorkItem", item.Id + ":" + item.Id, item.FormReceiptKey(item.Id, actor), answers, actor.ToString(), default)).IsSuccess.Should().BeTrue();
        answers["a.b"] = 43;
        (await guard.ValidateAsync(formId, 2, "WorkItem", item.Id + ":" + item.Id, item.FormReceiptKey(item.Id, actor), answers, actor.ToString(), default)).IsFailure.Should().BeTrue();
        answers["a.b"] = 42;
        (await guard.ValidateAsync(formId, 2, "WorkItem", item.Id.ToString(), item.Id, answers, actor.ToString(), default)).IsFailure.Should().BeTrue();
        (await guard.ValidateAsync(formId, 3, "WorkItem", item.Id + ":" + item.Id, item.FormReceiptKey(item.Id, actor), answers, actor.ToString(), default)).IsFailure.Should().BeTrue();
        (await guard.ValidateAsync(formId, 2, "WorkItem", item.Id + ":" + item.Id, item.FormReceiptKey(item.Id, actor), answers, Guid.NewGuid().ToString(), default)).IsFailure.Should().BeTrue();
        (await guard.ValidateAsync(formId, 2, "Standalone", null, item.Id, answers, actor.ToString(), default)).IsFailure.Should().BeTrue();
        (await guard.ValidateAsync(formId, 2, "WorkItem", Guid.NewGuid().ToString(), item.Id, answers, actor.ToString(), default)).IsFailure.Should().BeTrue();
        await AssertReadonlySubmissionPathsRejectVariants(item, formId, actor, guard);
        var oldKey = item.FormReceiptKey(item.Id, actor); var before = item.FormProgressJson; var recipient = Guid.NewGuid();
        item.Delegate(recipient, DateTime.UtcNow); await _db.SaveChangesAsync();
        var newKey = item.FormReceiptKey(item.Id, recipient); newKey.Should().NotBe(oldKey);
        await s.Runtime.PrepareStepAsync(item, Config(config), default);
        item.FormProgressJson.Should().Be(before);
        JsonSerializer.Serialize((await s.Runtime.ReadStepAsync(item, item.Id, default))!.Values).Should().Be(JsonSerializer.Serialize(snapshot.Values));
        (await guard.ValidateAsync(formId, 2, "WorkItem", item.Id + ":" + item.Id, newKey, answers, recipient.ToString(), default)).IsSuccess.Should().BeTrue();
        (await guard.ValidateAsync(formId, 2, "WorkItem", item.Id + ":" + item.Id, oldKey, answers, actor.ToString(), default)).IsFailure.Should().BeTrue();
        (await guard.ValidateAsync(formId, 2, "Standalone", null, oldKey, answers, actor.ToString(), default)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task SourceResolverUsesExactFieldIdentityAndCurrentBranchVisitOnly()
    {
        var s = MappingServices(); var seed = await TypedSeed(Guid.NewGuid()); var now = DateTime.UtcNow; var actor = Guid.NewGuid();
        var root = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "branch-root", "order", "activity", now, startedByUserId: actor);
        var first = WorkflowExecutionToken.Create(_orgId, root.Id, "fork", "first", now);
        var other = WorkflowExecutionToken.Create(_orgId, root.Id, "fork", "other", now);
        ActivityInstance Visit(string key, Guid token, int seconds)
        {
            var activity = ActivityInstance.Start(_orgId, root.Id, key, ActivityType.ServiceTask, key, now.AddSeconds(seconds)); activity.AttachToken(token); return activity;
        }
        var old = Visit("producer", first.Id, 1); var current = Visit("producer", first.Id, 2); var alien = Visit("producer", other.Id, 3); var origin = Visit("activity", first.Id, 4);
        _db.AddRange(root, first, other, old, current, alien, origin);
        foreach (var (activity, value) in new[] { (old, 1), (current, 2), (alien, 99) })
            _db.RoutingResults.Add(new()
            {
                Id = Guid.NewGuid(),
                OrganizationId = _orgId,
                WorkflowInstanceId = root.Id,
                ActivityInstanceId = activity.Id,
                ExecutionTokenId = activity.ExecutionTokenId,
                NodeKey = "producer",
                ValuesJson = JsonSerializer.Serialize(new Dictionary<string, object> { ["a.b"] = value }),
                CreatedAt = activity.StartedAt
            });
        var invocation = WorkflowMappingInvocation.Create(_orgId, root.Id, origin.Id, first.Id, actor, root.Id, seed.Version.Id, "activity", "Graph", seed.Node.ConfigurationJson!, "branch-invocation", now.AddSeconds(4));
        _db.Add(invocation); await _db.SaveChangesAsync();
        var source = new SourceReference(seed.Version.WorkflowDefinitionId, seed.Version.Id, "producer", "EventOutput", "a.b");
        var context = new MappingResolutionContext(_orgId, actor, root.Id, invocation.Id, now, new Dictionary<string, FrozenRelatedSelection>());
        var resolved = await s.Resolver.ResolveAsync(source, context, default);
        resolved.Value.GetInt32().Should().Be(2); resolved.ResultId.Should().Be((await _db.RoutingResults.SingleAsync(r => r.ActivityInstanceId == current.Id)).Id);
        (await s.Resolver.ResolveAsync(source with { FieldKey = "a" }, context, default)).State.Should().Be("Missing");
        var parentCall = WorkflowMappingInvocation.Create(_orgId, root.Id, origin.Id, first.Id, actor, root.Id, seed.Version.Id, "producer", "Callable", seed.Node.ConfigurationJson!, "parent-call", now);
        parentCall.Complete("{\"a.b\":11}", now);
        var unrelatedCall = WorkflowMappingInvocation.Create(_orgId, root.Id, origin.Id, first.Id, actor, root.Id, seed.Version.Id, "producer", "Callable", seed.Node.ConfigurationJson!, "unrelated-call", now);
        unrelatedCall.Complete("{\"a.b\":77}", now.AddSeconds(1));
        var childCall = WorkflowMappingInvocation.Create(_orgId, root.Id, origin.Id, first.Id, actor, root.Id, seed.Version.Id, "activity", "Callable", seed.Node.ConfigurationJson!, "child-call", now, parentCall.Id, "action");
        _db.AddRange(parentCall, unrelatedCall, childCall); await _db.SaveChangesAsync();
        var lineage = await s.Resolver.ResolveAsync(source, context with { InvocationId = childCall.Id }, default);
        lineage.Value.GetInt32().Should().Be(11); lineage.InvocationId.Should().Be(parentCall.Id);
        var hierarchy = OrgHierarchy.Build([("B", "C"), ("OTHER", "C2")], [], []);
        s.Access.Setup(a => a.GetAsync(actor, It.IsAny<CancellationToken>())).ReturnsAsync(new WorkflowActorAccess([NwfmPolicies.ViewInstances], OrgScopeSet.FromRows([new("Cbu", "OTHER", [], [])], hierarchy), hierarchy));
        Func<Task> forbidden = async () => await s.Resolver.ResolveAsync(source, context, default);
        await forbidden.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task MapOnlyReceiptRetainsPayloadWithoutPreparingTargetRequestOrSending()
    {
        var s = MappingServices(); var seed = await TypedSeed(Guid.NewGuid(), EmptyMapping() with { Callable = true, InvocationInputs = new("object", Properties: new() { ["literal"] = new("integer", Required: true) }) }); var actor = Guid.NewGuid();
        var root = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "map-root", "order", "activity", DateTime.UtcNow, startedByUserId: actor);
        var origin = ActivityInstance.Start(_orgId, root.Id, "activity", ActivityType.ServiceTask, "Activity", DateTime.UtcNow);
        var call = WorkflowMappingInvocation.Create(_orgId, root.Id, origin.Id, null, actor, root.Id, seed.Version.Id, "activity", "MapOnly", seed.Node.ConfigurationJson!, "map-only", DateTime.UtcNow, inputs: "{\"literal\":7}");
        _db.AddRange(root, origin, call); await _db.SaveChangesAsync();
        await s.Runtime.RecoverAsync(default);
        call.Applied.Should().BeTrue(); call.InvocationInputsJson.Should().Be("{\"literal\":7}"); call.PreparedRequestJson.Should().BeNull();
        (await _db.IntegrationJobs.CountAsync()).Should().Be(0); (await _db.RoutingResults.CountAsync()).Should().Be(0); origin.Status.Should().Be(ActivityInstanceStatus.Active);
        var prepared = await s.Runtime.ReadPreparedInputsAsync(call.Id, actor, default);
        prepared.IsSuccess.Should().BeTrue(); prepared.Value.InvocationId.Should().Be(call.Id); prepared.Value.Inputs.GetProperty("literal").GetInt32().Should().Be(7);
        s.Access.Setup(a => a.GetAsync(actor, It.IsAny<CancellationToken>())).ReturnsAsync((WorkflowActorAccess?)null);
        (await s.Runtime.ReadPreparedInputsAsync(call.Id, actor, default)).IsFailure.Should().BeTrue();
        (await _db.IntegrationJobs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task PublicationValidatesActualTypedPinsAndAcceptsExecutableConfiguration()
    {
        var seed = await TypedSeed(Guid.NewGuid()); var owner = new CallableTarget(seed.Version.WorkflowDefinitionId, seed.Version.Id, "activity");
        seed.Version.Activities.Should().Contain(seed.Node);
        var catalogs = new Mock<IDesignerMappingCatalog>(); catalogs.Setup(c => c.GetAsync(seed.Version.WorkflowDefinitionId, seed.Version.Id, default))
            .ReturnsAsync(Result.Success(new MappingCatalog([], [], [owner])));
        var validator = new MappingPublicationValidator(catalogs.Object);
        (await validator.ValidateAsync(seed.Version, default)).Should().BeEmpty();
        var invalid = EmptyMapping() with { Inputs = [new("body", "$body", new("source", Source: new(Guid.NewGuid(), Guid.NewGuid(), "foreign", "FormField", "secret")))] };
        seed.Node.SetPublishedConfiguration(Config(new HttpActivityConfiguration { MappingMode = "Typed", ConnectionId = Guid.NewGuid(), TypedMapping = invalid }));
        (await validator.ValidateAsync(seed.Version, default)).Should().Contain(e => e.Code == "MAPPING_INVALID");
    }

    [Fact]
    public async Task TransportRetryUsesIdenticalPreparedPayloadAndCompletesGraphOnce()
    {
        var s = MappingServices(); using var local = new MappingLocalServer(503, 200);
        var connection = await Connection(s.Api, "Http", local.Address, "None"); var seed = await TypedSeed(connection);
        var http = IntegrationJson.Read<HttpActivityConfiguration>(seed.Node.ConfigurationJson); http.MaxAttempts = 2;
        seed.Node.SetPublishedConfiguration(Config(http)); await _db.SaveChangesAsync();
        var engine = BuildEngine(_db, s.Api);
        (await engine.StartAsync(_orgId, seed.Binding.Id, "order", "retry", DateTime.UtcNow, startedByUserId: Guid.NewGuid(), pinnedWorkflowVersionId: seed.Version.Id)).IsSuccess.Should().BeTrue();
        var job = await _db.IntegrationJobs.SingleAsync(); var invocation = await _db.MappingInvocations.SingleAsync(); var frozen = invocation.PreparedRequestJson;
        var processor = new WorkflowIntegrationProcessor(_db, s.Api, new WorkflowIntegrationTransport(), engine, mappings: s.Runtime);
        await processor.ProcessJobAsync(job.Id, default); job.Status.Should().Be("Pending"); job.Attempts.Should().Be(1);
        job.Fail(job.Error!, job.StatusCode, DateTime.UtcNow.AddSeconds(-1)); await _db.SaveChangesAsync();
        await processor.ProcessJobAsync(job.Id, default); job.Status.Should().Be("Completed"); job.Attempts.Should().Be(2);
        invocation.PreparedRequestJson.Should().Be(frozen); local.Bodies.Should().HaveCount(2); local.Bodies[0].Should().Be(local.Bodies[1]);
        (await _db.RoutingResults.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DurableWaitTimeoutUsesPinnedOutcomeWithoutTransport()
    {
        var s = MappingServices(); var seed = await TypedSeed(Guid.NewGuid()); var actor = Guid.NewGuid();
        var mapping = EmptyMapping() with
        {
            Inputs = [new("body", "$body", new("object", Properties: new() { ["text"] = new("source",
            Source: new(seed.Version.WorkflowDefinitionId, seed.Version.Id, "$input", "WorkflowInput", "missing"), Missing: new("Wait", 1, "source-expired")) }))]
        };
        seed.Node.SetPublishedConfiguration(Config(new HttpActivityConfiguration { MappingMode = "Typed", TypedMapping = mapping }));
        var root = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "timeout-root", "order", "activity", DateTime.UtcNow, startedByUserId: actor);
        var origin = ActivityInstance.Start(_orgId, root.Id, "activity", ActivityType.ServiceTask, "Activity", DateTime.UtcNow);
        var invocation = WorkflowMappingInvocation.Create(_orgId, root.Id, origin.Id, null, actor, root.Id, seed.Version.Id, "activity", "Graph", seed.Node.ConfigurationJson!, "timeout", DateTime.UtcNow.AddMinutes(-5));
        invocation.Wait(DateTime.UtcNow.AddSeconds(-1), "missing source", "source-expired");
        _db.AddRange(root, origin, invocation); await _db.SaveChangesAsync();
        var engine = new Mock<IWorkflowRuntimeEngine>(MockBehavior.Strict);
        engine.Setup(e => e.CompleteExternalActivityAsync(origin.Id, It.IsAny<IReadOnlyDictionary<string, object?>>(), "source-expired", "Mapping wait expired.", It.IsAny<DateTime>(), default)).ReturnsAsync(Result.Success());
        s.Services.Setup(p => p.GetService(typeof(IWorkflowRuntimeEngine))).Returns(engine.Object);
        await s.Runtime.RecoverAsync(default); await s.Runtime.RecoverAsync(default);
        invocation.Status.Should().Be("Attention"); invocation.GraphAdvanced.Should().BeTrue();
        (await _db.IntegrationJobs.CountAsync()).Should().Be(0); engine.Verify(e => e.CompleteExternalActivityAsync(origin.Id, It.IsAny<IReadOnlyDictionary<string, object?>>(), "source-expired", "Mapping wait expired.", It.IsAny<DateTime>(), default), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypedTriggeredEventWaitsDurablyAndNeverCompletesItsParentStep(bool waitForSource)
    {
        var s = MappingServices(); using var local = new MappingLocalServer(); var actor = Guid.NewGuid();
        var connection = await Connection(s.Api, "Http", local.Address, "None");
        var seed = SeedSingleActivity(ActivityType.UserTask, "{\"slaDurationHours\":1}", DateTime.UtcNow);
        var version = (await _db.WorkflowVersions.FindAsync(seed.Version))!;
        version.SetWorkspace(Config(new WorkflowWorkspaceDefinition("Main", "C", "B", DesignerVersion: 4)));
        var mapping = EmptyMapping() with { Outputs = [new("out", "Output", new("number"), new("source", Source: new(version.WorkflowDefinitionId, version.Id, "typed-event", "Response", "$response", "/result")))] };
        if (waitForSource) mapping = mapping with { Inputs = [new("body", "$body", new("object", Properties: new() { ["text"] = new("source", Source: new(version.WorkflowDefinitionId, version.Id, "$input", "WorkflowInput", "later"), Missing: new("Wait", 300, "timeout")) }))] };
        var config = System.Text.Json.Nodes.JsonNode.Parse(Config(new HttpActivityConfiguration { ConnectionId = connection, MappingMode = "Typed", TypedMapping = mapping }))!.AsObject();
        config["triggerBinding"] = JsonSerializer.SerializeToNode(new { sourceNodeKey = "activity", trigger = "OnEnter" }); config["required"] = true;
        _db.Add(ActivityDefinition.Create(version.Id, "typed-event", ActivityType.ServiceTask, "Typed event", DateTime.UtcNow, actionKey: "http.request", configurationJson: config.ToJsonString()));
        await _db.SaveChangesAsync(); RoutingAssignments();
        var engine = BuildEngine(_db, s.Api, stepMappings: s.Runtime);
        var started = await engine.StartAsync(_orgId, seed.Binding.Id, "order", "triggered", DateTime.UtcNow, startedByUserId: actor, pinnedWorkflowVersionId: version.Id);
        started.IsSuccess.Should().BeTrue(started.IsFailure ? started.Error.Message : "");
        var parent = await _db.ActivityInstances.SingleAsync(a => a.ActivityNodeKey == "activity"); parent.Phase.Should().Be("WaitingForEnterEvents");
        (await _db.WorkItems.CountAsync()).Should().Be(0);
        var invocation = await _db.MappingInvocations.SingleAsync(); invocation.ExecutionKind.Should().Be("RequiredEvent"); invocation.TargetNodeKey.Should().Be("typed-event");
        if (waitForSource)
        {
            (await _db.IntegrationJobs.CountAsync()).Should().Be(0); invocation.Status.Should().Be("Waiting");
            await engine.ResumeActivityEventsAsync(parent.Id, default); (await _db.WorkItems.CountAsync()).Should().Be(0);
            var inputScope = await _db.RoutingScopes.SingleAsync(r => r.Id == started.Value.Id);
            var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(inputScope.ValuesJson)!; values["$input.later"] = JsonSerializer.SerializeToElement("registered");
            inputScope.ValuesJson = JsonSerializer.Serialize(values); await _db.SaveChangesAsync(); await s.Runtime.RecoverAsync(default);
        }
        var job = await _db.IntegrationJobs.SingleAsync(); job.Required.Should().BeTrue(); job.IsActivityEvent.Should().BeTrue();
        await new WorkflowIntegrationProcessor(_db, s.Api, new WorkflowIntegrationTransport(), engine, mappings: s.Runtime).ProcessJobAsync(job.Id, default);
        invocation.Applied.Should().BeTrue(); parent.Status.Should().Be(ActivityInstanceStatus.Active); (await _db.RoutingResults.CountAsync()).Should().Be(0);
        await engine.ResumeActivityEventsAsync(parent.Id, default); await engine.ResumeActivityEventsAsync(parent.Id, default);
        (await _db.WorkItems.CountAsync()).Should().Be(1); parent.Status.Should().Be(ActivityInstanceStatus.Active); local.Bodies.Should().ContainSingle();
        var consumer = WorkflowMappingInvocation.Create(_orgId, started.Value.Id, parent.Id, parent.ExecutionTokenId, actor, started.Value.Id, version.Id, "activity", "Graph", Config(new HttpActivityConfiguration { MappingMode = "Typed", TypedMapping = EmptyMapping() }), "consumer", DateTime.UtcNow);
        _db.Add(consumer); await _db.SaveChangesAsync();
        var output = await s.Resolver.ResolveAsync(new(version.WorkflowDefinitionId, version.Id, "typed-event", "EventOutput", "out"),
            new(_orgId, actor, started.Value.Id, consumer.Id, DateTimeOffset.UtcNow, new Dictionary<string, FrozenRelatedSelection>()), default);
        output.Value.GetInt32().Should().Be(7); output.InvocationId.Should().Be(invocation.Id);
    }

    [Fact]
    public async Task OptionalTypedGraphAdvancesOnceAndKeepsLateOutputsInItsInvocation()
    {
        var s = MappingServices(); using var local = new MappingLocalServer();
        var connection = await Connection(s.Api, "Http", local.Address, "None"); var seed = await TypedSeed(connection);
        var mapping = EmptyMapping() with { Outputs = [new("late", "Late output", new("number"), new("source", Source: new(seed.Version.WorkflowDefinitionId, seed.Version.Id, "activity", "Response", "$response", "/result")))] };
        var config = System.Text.Json.Nodes.JsonNode.Parse(Config(new HttpActivityConfiguration { MappingMode = "Typed", ConnectionId = connection, TypedMapping = mapping }))!.AsObject(); config["required"] = false;
        seed.Node.SetPublishedConfiguration(config.ToJsonString()); await _db.SaveChangesAsync();
        var engine = BuildEngine(_db, s.Api);
        var started = await engine.StartAsync(_orgId, seed.Binding.Id, "order", "optional", DateTime.UtcNow, startedByUserId: Guid.NewGuid(), pinnedWorkflowVersionId: seed.Version.Id);
        started.IsSuccess.Should().BeTrue(); started.Value.Status.Should().Be(WorkflowInstanceStatus.Completed);
        var graphReceipt = await _db.RoutingResults.SingleAsync(); var before = graphReceipt.ValuesJson;
        var job = await _db.IntegrationJobs.SingleAsync(); job.ExecutionKind.Should().Be("OptionalGraph");
        await new WorkflowIntegrationProcessor(_db, s.Api, new WorkflowIntegrationTransport(), engine, mappings: s.Runtime).ProcessJobAsync(job.Id, default);
        var invocation = await _db.MappingInvocations.SingleAsync(); invocation.Applied.Should().BeTrue(); invocation.GraphAdvanced.Should().BeTrue(); invocation.OutputsJson.Should().Contain("7");
        (await _db.RoutingResults.SingleAsync()).ValuesJson.Should().Be(before); local.Bodies.Should().ContainSingle();
    }

    private sealed class MappingLocalServer : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly Task loop;
        private readonly Queue<int> statuses;
        public List<string> Bodies { get; } = [];
        public string Address { get; }
        public MappingLocalServer(params int[] statusCodes)
        {
            statuses = new(statusCodes);
            listener.Start(); Address = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
            loop = Serve();
        }
        private async Task Serve()
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    using var socket = await listener.AcceptTcpClientAsync(stop.Token); await using var stream = socket.GetStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true); var length = 0;
                    string? line; while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(stop.Token)))
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line[15..].Trim());
                    var body = new char[length]; var read = 0; while (read < body.Length) { var n = await reader.ReadAsync(body.AsMemory(read), stop.Token); if (n == 0) throw new IOException("Incomplete local request."); read += n; }
                    Bodies.Add(new string(body));
                    var status = statuses.Count > 0 ? statuses.Dequeue() : 200;
                    var response = Encoding.UTF8.GetBytes($"HTTP/1.1 {status} Result\r\nContent-Type: application/json\r\nContent-Length: 12\r\nConnection: close\r\n\r\n{{\"result\":7}}");
                    await stream.WriteAsync(response, stop.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (SocketException) when (stop.IsCancellationRequested) { }
        }
        public void Dispose() { stop.Cancel(); listener.Stop(); loop.GetAwaiter().GetResult(); stop.Dispose(); }
    }
}
