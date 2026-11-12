namespace NWFM.Tests.Modules.Workflow;

using System.Text.Json;
using FluentAssertions;
using global::Workflow.Api.Controllers;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.Integrations;
using global::Workflow.Application.Mapping;
using global::Workflow.Application.Workspace;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Constants;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Organization;
using NWFM.Shared.Results;

public sealed partial class WorkflowRuntimeEnginePathTests
{
    private async Task<(WorkflowInstance Root, WorkflowInstance A, WorkflowInstance B, ActivityInstance Origin, WorkflowVersion AV, WorkflowVersion BV, ActivityDefinition ANode, WorkflowVersion RootVersion)> AuditSiblingTree(Guid connection)
    {
        var seed = await TypedSeed(connection, EmptyMapping() with { Callable = true, InvocationInputs = new("number") });
        var now = DateTime.UtcNow; var actor = Guid.NewGuid();
        var ad = WorkflowDefinition.Create(_orgId, "child-a", "A", now); var bd = WorkflowDefinition.Create(_orgId, "child-b", "B", now);
        var av = WorkflowVersion.CreateDraft(ad.Id, 1, actor, now); var bv = WorkflowVersion.CreateDraft(bd.Id, 1, actor, now);
        var at = Guid.NewGuid(); var bt = Guid.NewGuid();
        av.SetWorkspace(Config(new WorkflowWorkspaceDefinition("Child", "C", "B", DesignerVersion: 4) { ParentDefinitionId = seed.Version.WorkflowDefinitionId, ParentNodeKey = "slot-a", TaskTypeId = at }));
        bv.SetWorkspace(Config(new WorkflowWorkspaceDefinition("Child", "C", "B", DesignerVersion: 4) { ParentDefinitionId = seed.Version.WorkflowDefinitionId, ParentNodeKey = "slot-b", TaskTypeId = bt }));
        var root = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "sibling-root", "key", "slot-a", now.AddSeconds(-10), startedByUserId: actor);
        var aa = ActivityInstance.Start(_orgId, root.Id, "slot-a", ActivityType.MainActivity, "A", now.AddSeconds(-2));
        var ba = ActivityInstance.Start(_orgId, root.Id, "slot-b", ActivityType.MainActivity, "B", now.AddSeconds(-5)); ba.Complete(now.AddSeconds(-3));
        var a = WorkflowInstance.Start(_orgId, seed.Binding.Id, av.Id, "a", "key", "event", now.AddSeconds(-2), parentInstanceId: root.Id, parentActivityNodeKey: "slot-a"); a.AttachParentActivity(aa.Id);
        var b = WorkflowInstance.Start(_orgId, seed.Binding.Id, bv.Id, "b", "key", "producer", now.AddSeconds(-5), parentInstanceId: root.Id, parentActivityNodeKey: "slot-b"); b.AttachParentActivity(ba.Id); b.Complete(now.AddSeconds(-3));
        var origin = ActivityInstance.Start(_orgId, a.Id, "event", ActivityType.ServiceTask, "A event", now);
        var producer = ActivityInstance.Start(_orgId, b.Id, "producer", ActivityType.ServiceTask, "B producer", now.AddSeconds(-4)); producer.Complete(now.AddSeconds(-3));
        var receipt = WorkflowMappingInvocation.Create(_orgId, root.Id, producer.Id, null, actor, b.Id, bv.Id, "producer", "Graph", "{}", "prior-b", now.AddSeconds(-4)); receipt.Complete("{\"total\":7}", now.AddSeconds(-3)); receipt.MarkGraphAdvanced();
        var am = EmptyMapping() with { Inputs = [new("body", "$body", new("object", Properties: new() { ["text"] = new("text", Args: [new("source", Source: new(bd.Id, bv.Id, "producer", "EventOutput", "total"))]) }))], Actions = [new("to-b", new(bd.Id, bv.Id, "call"), "Run", Literal(7))] };
        var an = ActivityDefinition.Create(av.Id, "event", ActivityType.ServiceTask, "A event", now, actionKey: "http.request", configurationJson: Config(new HttpActivityConfiguration { ConnectionId = connection, MappingMode = "Typed", TypedMapping = am }));
        var bm = EmptyMapping() with { Callable = true, InvocationInputs = new("number"), Inputs = [new("body", "$body", new("object", Properties: new() { ["text"] = new("text", Args: [new("source", Source: new(bd.Id, bv.Id, "call", "InvocationInput", "$inputs"))]) }))] };
        _db.AddRange(ad, bd, av, bv, root, aa, ba, a, b, origin, producer, receipt, an,
            ActivityDefinition.Create(seed.Version.Id, "slot-a", ActivityType.MainActivity, "A", now, configurationJson: Config(new BusinessActivityConfiguration { VersionId = av.Id, TaskTypeId = at })),
            ActivityDefinition.Create(seed.Version.Id, "slot-b", ActivityType.MainActivity, "B", now, configurationJson: Config(new BusinessActivityConfiguration { VersionId = bv.Id, TaskTypeId = bt })),
            ActivityDefinition.Create(bv.Id, "call", ActivityType.ServiceTask, "B call", now, actionKey: "http.request", configurationJson: Config(new HttpActivityConfiguration { ConnectionId = connection, MappingMode = "Typed", TypedMapping = bm })));
        await _db.SaveChangesAsync(); return (root, a, b, origin, av, bv, an, seed.Version);
    }

    [Fact]
    public async Task SiblingTaskTypesReadAndCallPriorSiblingWithoutBorrowingOtherVisitsOrBranches()
    {
        var s = MappingServices(); using var local = new MappingLocalServer(); var connection = await Connection(s.Api, "Http", local.Address, "None");
        var tree = await AuditSiblingTree(connection);
        async Task<WorkflowInstance> OtherVisit(string slot, Guid version, int offset, bool branch = false)
        {
            var parent = ActivityInstance.Start(_orgId, tree.Root.Id, slot, ActivityType.MainActivity, slot, tree.Origin.StartedAt.AddSeconds(offset));
            if (branch) { var token = WorkflowExecutionToken.Create(_orgId, tree.Root.Id, "fork", "other", parent.StartedAt); _db.Add(token); parent.AttachToken(token.Id); }
            var child = WorkflowInstance.Start(_orgId, tree.Root.WorkflowBindingId, version, Guid.NewGuid().ToString(), "key", "call", parent.StartedAt, parentInstanceId: tree.Root.Id, parentActivityNodeKey: slot); child.AttachParentActivity(parent.Id);
            _db.AddRange(parent, child); await _db.SaveChangesAsync(); return child;
        }
        await OtherVisit("slot-a", tree.AV.Id, -20); await OtherVisit("slot-a", tree.AV.Id, 2);
        await OtherVisit("slot-b", tree.BV.Id, 2); await OtherVisit("slot-b", tree.BV.Id, -4, true);
        var future = ActivityInstance.Start(_orgId, tree.B.Id, "producer", ActivityType.ServiceTask, "Future visit", tree.Origin.StartedAt.AddSeconds(-1));
        var futureReceipt = WorkflowMappingInvocation.Create(_orgId, tree.Root.Id, future.Id, null, tree.Root.StartedByUserId!.Value, tree.B.Id, tree.BV.Id, "producer", "Graph", "{}", "future-b", future.StartedAt);
        futureReceipt.Complete("{\"total\":99}", future.StartedAt); futureReceipt.MarkGraphAdvanced(); _db.AddRange(future, futureReceipt); await _db.SaveChangesAsync();
        var engine = new Mock<IWorkflowRuntimeEngine>(); engine.Setup(e => e.CompleteExternalActivityAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyDictionary<string, object?>>(), "success", null, It.IsAny<DateTime>(), default)).ReturnsAsync(Result.Success());
        s.Services.Setup(p => p.GetService(typeof(IWorkflowRuntimeEngine))).Returns(engine.Object);
        (await s.Runtime.BeginGraphAsync(tree.A.Id, tree.Origin.Id, tree.ANode.ConfigurationJson!, "sibling-a", default)).IsSuccess.Should().BeTrue();
        var parent = await _db.MappingInvocations.SingleAsync(i => i.OperationKey == "sibling-a");
        parent.PreparedRequestJson.Should().Contain("7"); (await s.Resolver.TreeTargetAsync(parent, tree.AV.Id, default))!.Id.Should().Be(tree.A.Id);
        (await s.Resolver.TreeTargetAsync(parent, tree.BV.Id, default))!.Id.Should().Be(tree.B.Id);
        var processor = new WorkflowIntegrationProcessor(_db, s.Api, new WorkflowIntegrationTransport(), engine.Object, mappings: s.Runtime);
        await processor.ProcessJobAsync((await _db.IntegrationJobs.SingleAsync(j => j.InvocationId == parent.Id)).Id, default);
        var call = await _db.MappingInvocations.SingleAsync(i => i.ParentInvocationId == parent.Id); call.TargetInstanceId.Should().Be(tree.B.Id);
        await s.Runtime.RecoverAsync(default); await processor.ProcessJobAsync((await _db.IntegrationJobs.SingleAsync(j => j.InvocationId == call.Id)).Id, default);
        call.Applied.Should().BeTrue(); local.Bodies.Should().Equal("{\"text\":\"7\"}", "{\"text\":\"7\"}");
        engine.Verify(e => e.CompleteExternalActivityAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyDictionary<string, object?>>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTime>(), default), Times.Once);
    }

    [Fact]
    public async Task ReceiptsListedFromAChildInstanceComeFromItsAuthorizedRootTree()
    {
        var s = MappingServices(); using var local = new MappingLocalServer(); var connection = await Connection(s.Api, "Http", local.Address, "None");
        var tree = await AuditSiblingTree(connection); var actor = tree.Root.StartedByUserId!.Value;
        var permissions = new[] { NwfmPolicies.ViewInstances, NwfmPolicies.ViewSubmissions };
        s.Access.Setup(a => a.GetAsync(actor, default)).ReturnsAsync(new WorkflowActorAccess(permissions, OrgScopeSet.Unrestricted(), OrgHierarchy.Build([], [], [])));
        var fromChild = (await s.Runtime.ListAsync(tree.A.Id, actor, default)).Value; var fromRoot = (await s.Runtime.ListAsync(tree.Root.Id, actor, default)).Value;
        fromRoot.Should().NotBeEmpty(); fromChild.Select(r => r.Id).Should().Equal(fromRoot.Select(r => r.Id));
        fromChild.Should().OnlyContain(r => r.RootInstanceId == tree.Root.Id && r.OperationId == null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChildToParentFollowUpHonorsOriginSuspensionAndCancellationWithCompletedToken(bool cancel)
    {
        var s = MappingServices(); using var local = new MappingLocalServer(); var connection = await Connection(s.Api, "Http", local.Address, "None");
        var tree = await AuditSiblingTree(connection);
        var http = IntegrationJson.Read<HttpActivityConfiguration>(tree.ANode.ConfigurationJson);
        http.TypedMapping = http.TypedMapping! with { Actions = [new("to-parent", new(tree.RootVersion.WorkflowDefinitionId, tree.RootVersion.Id, "activity"), "Run", Literal(7))] };
        var token = WorkflowExecutionToken.Create(_orgId, tree.A.Id, "fork", "origin", tree.Origin.StartedAt); tree.Origin.AttachToken(token.Id); _db.Add(token); await _db.SaveChangesAsync();
        var engine = new Mock<IWorkflowRuntimeEngine>(); engine.Setup(e => e.CompleteExternalActivityAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyDictionary<string, object?>>(), "success", null, It.IsAny<DateTime>(), default)).ReturnsAsync(Result.Success());
        s.Services.Setup(p => p.GetService(typeof(IWorkflowRuntimeEngine))).Returns(engine.Object);
        await s.Runtime.BeginGraphAsync(tree.A.Id, tree.Origin.Id, Config(http), "origin-a", default);
        var processor = new WorkflowIntegrationProcessor(_db, s.Api, new WorkflowIntegrationTransport(), engine.Object, mappings: s.Runtime);
        await processor.ProcessJobAsync((await _db.IntegrationJobs.SingleAsync()).Id, default);
        var call = await _db.MappingInvocations.SingleAsync(i => i.ActionId == "to-parent"); call.TargetInstanceId.Should().Be(tree.Root.Id);
        tree.A.Suspend(DateTime.UtcNow); await _db.SaveChangesAsync(); await s.Runtime.RecoverAsync(default);
        (await _db.IntegrationJobs.CountAsync()).Should().Be(1); call.Status.Should().Be("Preparing");
        tree.A.Resume(DateTime.UtcNow); tree.A.Complete(DateTime.UtcNow); tree.Origin.Complete(DateTime.UtcNow); token.Complete(DateTime.UtcNow); await _db.SaveChangesAsync();
        await s.Runtime.RecoverAsync(default); var job = await _db.IntegrationJobs.SingleAsync(j => j.InvocationId == call.Id);
        if (cancel) { tree.A.Cancel(DateTime.UtcNow); await _db.SaveChangesAsync(); }
        await processor.ProcessJobAsync(job.Id, default);
        call.Status.Should().Be(cancel ? "Cancelled" : "Completed"); job.Status.Should().Be(cancel ? "Cancelled" : "Completed");
        local.Bodies.Should().HaveCount(cancel ? 1 : 2); token.Status.Should().Be(ExecutionTokenStatus.Completed);
    }

    [Fact]
    public async Task FutureSiblingActivationUsesUnavailablePolicyAndNeverCreatesChild()
    {
        var s = MappingServices(); using var local = new MappingLocalServer(); var connection = await Connection(s.Api, "Http", local.Address, "None");
        var tree = await AuditSiblingTree(connection);
        var futureParent = ActivityInstance.Start(_orgId, tree.Root.Id, "slot-b", ActivityType.MainActivity, "Future B", tree.Origin.StartedAt.AddSeconds(1));
        tree.B.AttachParentActivity(futureParent.Id); _db.Add(futureParent); await _db.SaveChangesAsync();
        var mapping = EmptyMapping() with { Actions = [new("future", new(tree.BV.WorkflowDefinitionId, tree.BV.Id, "call"), "Run", Literal(7), new("Wait", 172800, "future-timeout"))] };
        var engine = new Mock<IWorkflowRuntimeEngine>(); engine.Setup(e => e.CompleteExternalActivityAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyDictionary<string, object?>>(), "success", null, It.IsAny<DateTime>(), default)).ReturnsAsync(Result.Success());
        s.Services.Setup(p => p.GetService(typeof(IWorkflowRuntimeEngine))).Returns(engine.Object);
        await s.Runtime.BeginGraphAsync(tree.A.Id, tree.Origin.Id, Config(new HttpActivityConfiguration { ConnectionId = connection, MappingMode = "Typed", TypedMapping = mapping }), "future-parent", default);
        await new WorkflowIntegrationProcessor(_db, s.Api, new WorkflowIntegrationTransport(), engine.Object, mappings: s.Runtime).ProcessJobAsync((await _db.IntegrationJobs.SingleAsync()).Id, default);
        var call = await _db.MappingInvocations.SingleAsync(i => i.ActionId == "future");
        call.TargetInstanceId.Should().Be(Guid.Empty); call.Status.Should().Be("Waiting");
        await s.Runtime.RecoverAsync(default); call.Deadline.Should().Be(call.WindowAnchor.AddHours(48)); call.TimeoutOutcome.Should().Be("future-timeout");
        (await _db.WorkflowInstances.CountAsync()).Should().Be(3); (await _db.IntegrationJobs.CountAsync()).Should().Be(1); local.Bodies.Should().HaveCount(1);
    }

    [Theory]
    [InlineData("X", "X")]
    [InlineData("X", "Y")]
    [InlineData("Y", "X")]
    public async Task ActualLocationAuthorizesFormAndStepAndRechecksFrozenValues(string stepDepartment, string formDepartment)
    {
        var s = MappingServices(); var actor = Guid.NewGuid(); using var local = new MappingLocalServer();
        var connection = await Connection(s.Api, "Http", local.Address, "None"); var seed = await TypedSeed(connection);
        seed.Version.SetWorkspace(Config(new WorkflowWorkspaceDefinition("Main", DesignerVersion: 4) { OrganizationScopes = [new("Cbu", "A", "C", "A"), new("Cbu", "B", "C", "B")] }));
        var formId = Guid.NewGuid(); var typeId = Guid.NewGuid(); var now = DateTime.UtcNow;
        var formConfig = new BusinessActivityConfiguration { TaskTypeId = typeId, DepartmentCode = stepDepartment, FieldActivityCode = "work", FormSteps = [new() { Id = "form", FormId = formId, VersionNo = 1, Name = "Form" }] };
        _db.Add(ActivityDefinition.Create(seed.Version.Id, "step", ActivityType.UserTask, "Step", now, configurationJson: Config(formConfig)));
        var root = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "classified", "key", "activity", now, startedByUserId: actor);
        root.SetExecutionContext(new("C", "B"), false);
        var submitted = ActivityInstance.Start(_orgId, root.Id, "step", ActivityType.UserTask, "Step", now.AddSeconds(-2));
        var origin = ActivityInstance.Start(_orgId, root.Id, "activity", ActivityType.ServiceTask, "Event", now);
        var item = WorkItem.Create(_orgId, root.Id, submitted.Id, Guid.NewGuid(), now); var submission = Guid.NewGuid(); var visit = Guid.NewGuid();
        item.SetFormProgress(JsonSerializer.Serialize(new ActivityFormProgress { ActiveStepId = "form", VisitId = visit, ExitNodeKey = "done", Visits = [new() { StepId = "form", FormId = formId, VersionNo = 1, VisitId = visit, SubmissionId = submission }] }));
        var source = new SourceReference(seed.Version.WorkflowDefinitionId, seed.Version.Id, "step", "FormField", "text", FormStepId: "form", FormId: formId, FormVersion: 1);
        var mapping = EmptyMapping() with { Inputs = [new("body", "$body", new("object", Properties: new() { ["text"] = new("source", Source: source, Missing: new("Fallback", Fallback: JsonSerializer.SerializeToElement("must-not-use"))) }))] };
        var config = Config(new HttpActivityConfiguration { ConnectionId = connection, MappingMode = "Typed", TypedMapping = mapping });
        var invocation = WorkflowMappingInvocation.Create(_orgId, root.Id, origin.Id, null, actor, root.Id, seed.Version.Id, "activity", "Callable", config, "classified-call", now);
        _db.AddRange(root, submitted, origin, item, invocation); await _db.SaveChangesAsync();
        var types = new Mock<IWorkflowTaskTypeCatalog>(); types.Setup(t => t.FormIdsAsync(typeId, default)).ReturnsAsync([formId]);
        s.Forms.Setup(f => f.FindFieldActivityFormAsync(formId, default)).ReturnsAsync(new FieldActivityFormInfo(formId, "F", "Form", "Form", "", "Published", formDepartment, "work", 1, [1], true, now));
        s.Forms.Setup(f => f.GetFieldsAsync(formId, 1, default)).ReturnsAsync([new("text", "text", "Text", null, null, [])]);
        s.Forms.Setup(f => f.GetByClientIdAsync(formId, visit, "WorkItem", item.Id + ":" + visit, default)).ReturnsAsync(new FormSubmissionRecord(submission, formId, 1, actor.ToString(), "Actor", now, new Dictionary<string, object?> { ["text"] = "private" }));
        var hierarchy = OrgHierarchy.Build([("A", "C"), ("B", "C")], [], []);
        var permissions = new[] { NwfmPolicies.ViewInstances, NwfmPolicies.ViewSubmissions };
        var mixed = new WorkflowActorAccess(permissions, OrgScopeSet.FromRows([new("Cbu", "A", ["X"], ["work"]), new("Cbu", "B", ["Y"], ["work"])], hierarchy), hierarchy);
        s.Access.Setup(a => a.GetAsync(actor, default)).ReturnsAsync(mixed);
        var resolver = new MappingSourceResolver(_db, s.Access.Object, s.Forms.Object, types.Object);
        var runtime = new WorkflowMappingRuntime(_db, resolver, s.Services.Object);
        await runtime.PrepareAsync(invocation, default);
        invocation.Status.Should().Be("Attention"); (await _db.IntegrationJobs.CountAsync()).Should().Be(0);
        invocation.Error.Should().Contain("actual source instance location");
        s.Forms.Verify(f => f.GetLatestByContextAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
        s.Access.Setup(a => a.GetAsync(actor, default)).ReturnsAsync(new WorkflowActorAccess(permissions, OrgScopeSet.FromRows([new("Cbu", "B", ["X", "Y"], ["work"])], hierarchy), hierarchy));
        (await runtime.RetryAsync(invocation.Id, actor, default)).IsSuccess.Should().BeTrue(); await runtime.RecoverAsync(default);
        invocation.Status.Should().Be("Prepared"); invocation.PreparedRequestJson.Should().Contain("private");
        // The receipt names its delivering operation, so a client can offer operation replay instead of a retry the server refuses.
        var receipt = (await runtime.ListAsync(invocation.RootInstanceId, actor, default)).Value.Single();
        receipt.OperationId.Should().Be((await _db.IntegrationJobs.SingleAsync()).Id);
        s.Access.Setup(a => a.GetAsync(actor, default)).ReturnsAsync(mixed);
        var engine = new Mock<IWorkflowRuntimeEngine>(MockBehavior.Strict);
        await new WorkflowIntegrationProcessor(_db, s.Api, new WorkflowIntegrationTransport(), engine.Object, mappings: runtime).ProcessJobAsync((await _db.IntegrationJobs.SingleAsync()).Id, default);
        invocation.Status.Should().Be("Attention"); local.Bodies.Should().BeEmpty(); engine.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TimeoutRetryApiCannotSendAfterGraphAdvanceWhenDataArrivesLater()
    {
        var s = MappingServices(); using var local = new MappingLocalServer(); var actor = Guid.NewGuid();
        var connection = await Connection(s.Api, "Http", local.Address, "None"); var seed = await TypedSeed(connection);
        var source = new SourceReference(seed.Version.WorkflowDefinitionId, seed.Version.Id, "$input", "WorkflowInput", "late");
        var mapping = EmptyMapping() with { Inputs = [new("body", "$body", new("object", Properties: new() { ["text"] = new("source", Source: source, Missing: new("Wait", 1, "timeout")) }))] };
        seed.Node.SetPublishedConfiguration(Config(new HttpActivityConfiguration { ConnectionId = connection, MappingMode = "Typed", TypedMapping = mapping })); await _db.SaveChangesAsync();
        var engine = BuildEngine(_db, s.Api); s.Services.Setup(p => p.GetService(typeof(IWorkflowRuntimeEngine))).Returns(engine);
        var started = await engine.StartAsync(_orgId, seed.Binding.Id, "order", "timeout-audit", DateTime.UtcNow, startedByUserId: actor, pinnedWorkflowVersionId: seed.Version.Id);
        started.IsSuccess.Should().BeTrue(); var invocation = await _db.MappingInvocations.SingleAsync(); invocation.Status.Should().Be("Waiting");
        await Task.Delay(1100); await s.Runtime.RecoverAsync(default);
        invocation.GraphAdvanced.Should().BeTrue(); invocation.Status.Should().Be("Attention");
        (await _db.WorkflowInstances.FindAsync(started.Value.Id))!.Status.Should().Be(WorkflowInstanceStatus.Completed);
        var activity = await _db.ActivityInstances.FindAsync(invocation.OriginActivityId); activity!.Status.Should().NotBe(ActivityInstanceStatus.Active);
        var scope = await _db.RoutingScopes.SingleAsync(r => r.Id == started.Value.Id); scope.ValuesJson = "{\"$input.late\":\"now available\"}"; await _db.SaveChangesAsync();
        var provider = new ServiceCollection().AddSingleton<ICurrentTenant>(new StubTenant(_orgId)).AddSingleton(Mock.Of<IWorkflowActorContext>(a => a.ActorId == actor)).BuildServiceProvider();
        var controller = new WorkflowMappingInvocationsController(s.Runtime) { ControllerContext = new() { HttpContext = new DefaultHttpContext { RequestServices = provider } } };
        (await controller.Retry(invocation.Id, default)).Should().BeOfType<BadRequestObjectResult>();
        await s.Runtime.PrepareAsync(invocation, default); await s.Runtime.RecoverAsync(default);
        (await s.Runtime.CanDeliverAsync(invocation, default)).Should().BeFalse(); (await _db.IntegrationJobs.CountAsync()).Should().Be(0); local.Bodies.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolvedWaitsDoNotShortenRemainingDependenciesAcrossRestartAndRetry()
    {
        var s = MappingServices(); using var local = new MappingLocalServer(); var actor = Guid.NewGuid();
        var connection = await Connection(s.Api, "Http", local.Address, "None"); var seed = await TypedSeed(connection);
        var definition = WorkflowDefinition.Create(_orgId, "wait-source", "Source", DateTime.UtcNow); var version = WorkflowVersion.CreateDraft(definition.Id, 1, actor, DateTime.UtcNow);
        version.SetWorkspace(Config(new WorkflowWorkspaceDefinition("Main", "C", "B", DesignerVersion: 4)));
        var anchor = DateTime.UtcNow.AddSeconds(-45);
        TransformNode Field(string field, int seconds) => new("source", Source: new(definition.Id, version.Id, field, "RoutingOutput", "value", SelectorId: "related"), Missing: new("Wait", seconds, field + "-expired"));
        var mapping = EmptyMapping() with
        {
            Selectors = [new("related", definition.Id, version.Id, "BusinessKey", Literal("source"), Missing: new("Wait", 10, "selector-expired")),
                new("independent", definition.Id, version.Id, "BusinessKey", Literal("independent"), Missing: new("Wait", 5, "independent-expired"))],
            Inputs = [new("body", "$body", new("object", Properties: new() { ["text"] = new("concat", Args: [Field("fast", 40), Field("slow", 60)]) }))]
        };
        var config = Config(new HttpActivityConfiguration { ConnectionId = connection, MappingMode = "Typed", TypedMapping = mapping });
        var root = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "wait-root", "key", "activity", anchor, startedByUserId: actor);
        var origin = ActivityInstance.Start(_orgId, root.Id, "activity", ActivityType.ServiceTask, "Event", anchor);
        var invocation = WorkflowMappingInvocation.Create(_orgId, root.Id, origin.Id, null, actor, root.Id, seed.Version.Id, "activity", "Callable", config, "wait-dependencies", anchor);
        var independent = WorkflowInstance.Start(_orgId, Guid.NewGuid(), version.Id, "independent", "independent", "$input", anchor.AddSeconds(1));
        _db.AddRange(definition, version, root, origin, invocation, independent); await _db.SaveChangesAsync();
        await s.Runtime.PrepareAsync(invocation, default); invocation.Deadline.Should().Be(anchor.AddSeconds(10));
        invocation.SelectionsJson.Should().Contain(independent.Id.ToString());
        var related = WorkflowInstance.Start(_orgId, Guid.NewGuid(), version.Id, "registered", "source", "$input", anchor.AddSeconds(2)); _db.Add(related); await _db.SaveChangesAsync();
        await s.Runtime.PrepareAsync(invocation, default); invocation.Deadline.Should().Be(anchor.AddSeconds(40));
        var pending = JsonSerializer.Deserialize<List<WorkflowMappingWait>>(invocation.WaitDependenciesJson)!;
        pending.Should().HaveCount(2); pending.Should().OnlyContain(w => w.Anchor == anchor); pending.Select(w => w.Deadline).Should().Contain(anchor.AddSeconds(60));
        var fast = ActivityInstance.Start(_orgId, related.Id, "fast", ActivityType.UserTask, "Fast", anchor.AddSeconds(3));
        var slow = ActivityInstance.Start(_orgId, related.Id, "slow", ActivityType.UserTask, "Slow", anchor.AddSeconds(4));
        // Availability is the immutable result timestamp, not when this recovering worker observes it.
        _db.AddRange(fast, slow, new WorkflowRoutingResult
        {
            Id = Guid.NewGuid(),
            OrganizationId = _orgId,
            WorkflowInstanceId = related.Id,
            ActivityInstanceId = fast.Id,
            NodeKey = "fast",
            ValuesJson = "{\"value\":\"first\"}",
            CreatedAt = anchor.AddSeconds(30)
        });
        await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();
        var restarted = MappingServices(); await restarted.Runtime.RecoverAsync(default); invocation = await _db.MappingInvocations.SingleAsync();
        invocation.Status.Should().Be("Waiting"); invocation.Deadline.Should().Be(anchor.AddSeconds(60)); invocation.TimeoutOutcome.Should().Be("slow-expired");
        JsonSerializer.Deserialize<List<WorkflowMappingWait>>(invocation.WaitDependenciesJson)!.Should().ContainSingle();
        invocation.Attention("retryable preparation fault"); await _db.SaveChangesAsync();
        (await restarted.Runtime.RetryAsync(invocation.Id, actor, default)).IsSuccess.Should().BeTrue(); await restarted.Runtime.RecoverAsync(default);
        invocation.Deadline.Should().Be(anchor.AddSeconds(60)); invocation.WindowAnchor.Should().Be(anchor);
        _db.Add(new WorkflowRoutingResult
        {
            Id = Guid.NewGuid(),
            OrganizationId = _orgId,
            WorkflowInstanceId = related.Id,
            ActivityInstanceId = slow.Id,
            NodeKey = "slow",
            ValuesJson = "{\"value\":\"second\"}",
            CreatedAt = anchor.AddSeconds(44)
        }); await _db.SaveChangesAsync();
        await restarted.Runtime.RecoverAsync(default); invocation.Deadline.Should().BeNull(); invocation.WaitDependenciesJson.Should().Be("[]"); invocation.Status.Should().Be("Prepared");
        invocation.PreparedRequestJson.Should().Contain("firstsecond").And.NotContain("changed");
        await new WorkflowIntegrationProcessor(_db, s.Api, new WorkflowIntegrationTransport(), Mock.Of<IWorkflowRuntimeEngine>(), mappings: restarted.Runtime).ProcessJobAsync((await _db.IntegrationJobs.SingleAsync()).Id, default);
        local.Bodies.Should().ContainSingle().Which.Should().Contain("firstsecond");
    }
}
