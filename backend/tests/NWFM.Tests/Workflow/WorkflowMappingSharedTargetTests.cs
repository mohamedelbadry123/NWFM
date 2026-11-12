namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.Integrations;
using global::Workflow.Application.Mapping;
using global::Workflow.Application.Workspace;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Infrastructure.Persistence.Repositories;
using global::Workflow.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using NWFM.Shared.Integration.Workflow;

public sealed partial class WorkflowRuntimeEnginePathTests
{
    [Fact]
    public async Task SharedCallableTargetKeepsEachCommentActionAndNeverAdvancesOriginGraph()
    {
        var s = MappingServices(); using var local = new MappingLocalServer();
        var connection = await Connection(s.Api, "Http", local.Address, "None"); var now = DateTime.UtcNow; var actor = Guid.NewGuid();
        var seed = SeedSingleActivity(ActivityType.UserTask, Config(new BusinessActivityConfiguration { SlaDurationHours = 1 }), now);
        var version = (await _db.WorkflowVersions.FindAsync(seed.Version))!;
        version.SetWorkspace(Config(new WorkflowWorkspaceDefinition("Main", "C", "B", DesignerVersion: 4))); RoutingAssignments();
        var target = new CallableTarget(version.WorkflowDefinitionId, version.Id, "receiver");
        var mapping = EmptyMapping() with { Actions = [new("prepared", target, "MapOnly", Literal(11)), new("run", target, "Run", Literal(22)), new("another-run", target, "Run", Literal(33))] };
        var receiver = EmptyMapping() with
        {
            Callable = true,
            InvocationInputs = new("number"),
            Inputs = [new("body", "$body", new("object", Properties: new() { ["text"] = new("text", Args: [new("source", Source: new(target.WorkflowDefinitionId, target.WorkflowVersionId, target.NodeKey, "InvocationInput", "$inputs"))]) }))]
        };
        _db.AddRange(ActivityDefinition.Create(version.Id, "caller", ActivityType.ServiceTask, "Caller", now, actionKey: "http.request", configurationJson: Config(new
        { connectionId = connection, mappingMode = "Typed", typedMapping = mapping, required = false, triggerBinding = new { sourceNodeKey = "activity", trigger = "OnComment" } })),
            ActivityDefinition.Create(version.Id, target.NodeKey, ActivityType.ServiceTask, "Receiver", now, actionKey: "http.request", configurationJson: Config(new HttpActivityConfiguration { ConnectionId = connection, MappingMode = "Typed", TypedMapping = receiver })));
        await _db.SaveChangesAsync();
        var catalogs = new DesignerMappingCatalog(_db, new WorkflowVersionRepository(_db), new StubTenant(_orgId),
            Mock.Of<NWFM.Shared.Abstractions.ICurrentUser>(u => u.Id == actor.ToString()), s.Access.Object, s.Forms.Object, Mock.Of<IWorkflowTaskTypeCatalog>());
        (await new MappingPublicationValidator(catalogs).ValidateAsync(version, default)).Should().BeEmpty();
        var engine = BuildEngine(_db, s.Api, stepMappings: s.Runtime); s.Services.Setup(p => p.GetService(typeof(IWorkflowRuntimeEngine))).Returns(engine);
        var root = (await engine.StartAsync(_orgId, seed.Binding.Id, "order", "shared-target", now, startedByUserId: actor, pinnedWorkflowVersionId: version.Id)).Value;
        var origin = await _db.ActivityInstances.SingleAsync(a => a.ActivityNodeKey == "activity");
        var definition = await _db.ActivityDefinitions.SingleAsync(a => a.WorkflowVersionId == version.Id && a.NodeKey == "activity");
        var activities = await _db.ActivityInstances.CountAsync(); var tokens = await _db.WorkflowExecutionTokens.CountAsync(); var item = await _db.WorkItems.SingleAsync();
        var events = new WorkflowActivityEvents(_db, s.Api, s.Runtime);
        (await events.QueueAsync(root, definition, origin, "OnComment", "one", default)).IsSuccess.Should().BeTrue();
        var processor = new WorkflowIntegrationProcessor(_db, s.Api, new WorkflowIntegrationTransport(), engine, events, s.Runtime);
        var caller = await _db.IntegrationJobs.SingleAsync(); await processor.ProcessJobAsync(caller.Id, default); await processor.ProcessJobAsync(caller.Id, default);
        caller.Status.Should().Be("Completed");
        await s.Runtime.RecoverAsync(default); await s.Runtime.RecoverAsync(default);
        var children = await _db.MappingInvocations.Where(i => i.ParentInvocationId == caller.InvocationId).ToListAsync();
        children.Select(c => c.ActionId).Should().BeEquivalentTo(new[] { "prepared", "run", "another-run" });
        children.Should().OnlyContain(c => c.TargetNodeKey == target.NodeKey && c.TargetVersionId == target.WorkflowVersionId);
        var prepared = children.Single(c => c.ActionId == "prepared"); prepared.Applied.Should().BeTrue(); prepared.InvocationInputsJson.Should().Be("11");
        (await s.Runtime.ReadPreparedInputsAsync(prepared.Id, actor, default)).Value.Inputs.GetInt32().Should().Be(11);
        (await _db.IntegrationJobs.AnyAsync(j => j.InvocationId == prepared.Id)).Should().BeFalse();
        foreach (var id in new[] { "run", "another-run" })
        {
            var invocation = children.Single(c => c.ActionId == id);
            var job = await _db.IntegrationJobs.SingleAsync(j => j.InvocationId == invocation.Id);
            await processor.ProcessJobAsync(job.Id, default); await processor.ProcessJobAsync(job.Id, default); invocation.Applied.Should().BeTrue();
        }
        (await events.QueueAsync(root, definition, origin, "OnComment", "one", default)).IsSuccess.Should().BeTrue();
        await s.Runtime.RecoverAsync(default);
        local.Bodies.Should().Equal("{\"text\":\"{{literal}}\"}", "{\"text\":\"22\"}", "{\"text\":\"33\"}");
        (await _db.MappingInvocations.CountAsync()).Should().Be(4); (await _db.IntegrationJobs.CountAsync()).Should().Be(3);
        origin.Status.Should().Be(ActivityInstanceStatus.Active); item.Status.Should().Be(WorkItemStatus.Pending); root.Status.Should().Be(WorkflowInstanceStatus.Running);
        (await _db.ActivityInstances.CountAsync()).Should().Be(activities); (await _db.WorkflowExecutionTokens.CountAsync()).Should().Be(tokens);
        (await _db.RoutingResults.CountAsync()).Should().Be(0);
    }
}
