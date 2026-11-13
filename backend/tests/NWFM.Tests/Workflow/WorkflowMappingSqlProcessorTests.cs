namespace NWFM.Tests.Modules.Workflow;

using System.Data.Common;
using FluentAssertions;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.Integrations;
using global::Workflow.Application.Settings;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Infrastructure.Persistence;
using global::Workflow.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Moq;
using NWFM.Shared.Constants;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Organization;
using NWFM.Tests.Infrastructure;

public sealed partial class WorkflowRuntimeEnginePathTests
{
    private sealed class MappingCommitFault : DbTransactionInterceptor
    {
        public bool Fired { get; private set; }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (!Fired && eventData.Context!.ChangeTracker.Entries<WorkflowMappingInvocation>().Any(e => e.Entity.ExecutionKind == "Graph" && e.Entity.GraphAdvanced && e.Entity.Applied))
            { Fired = true; throw new IOException("Injected root commit loss after real graph completion."); }
            return ValueTask.FromResult(result);
        }
    }

    [MappingSqlFact]
    public async Task IndependentReviewSqlProcessorRecoversRealGraphCommitAndDispatchesOneFollowUp()
    {
        await using var sql = await OwnedLocalDb.CreateAsync();
        var connectionString = sql.ConnectionString;
        var fault = new MappingCommitFault();
        WorkflowDbContext Context(bool inject = false)
        {
            var options = new DbContextOptionsBuilder<WorkflowDbContext>().UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "Workflow"));
            if (inject) options.AddInterceptors(fault);
            return new(options.Options, new StubTenant(_orgId));
        }
        using var local = new MappingLocalServer(); var protection = new EphemeralDataProtectionProvider();
        var access = new Mock<IWorkflowActorAccess>(); access.Setup(a => a.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new WorkflowActorAccess([NwfmPolicies.ViewInstances], OrgScopeSet.Unrestricted(), OrgHierarchy.Empty));
        (WorkflowMappingRuntime Runtime, WorkflowIntegrations Api, WorkflowRuntimeEngine Engine, WorkflowIntegrationProcessor Processor) Services(WorkflowDbContext db)
        {
            var provider = new Mock<IServiceProvider>();
            var runtime = new WorkflowMappingRuntime(db, new MappingSourceResolver(db, access.Object, Mock.Of<IFormGateway>()), provider.Object);
            var api = new WorkflowIntegrations(db, new StubTenant(_orgId), protection, new WorkflowIntegrationTransport(), Options.Create(new WorkflowSettings { AllowPrivateConnections = true }), runtime);
            var engine = BuildEngine(db, api); provider.Setup(p => p.GetService(typeof(IWorkflowRuntimeEngine))).Returns(engine);
            return (runtime, api, engine, new(db, api, new WorkflowIntegrationTransport(), engine, new WorkflowActivityEvents(db, api, runtime), runtime));
        }
        Guid rootId, parentId, jobId;
        await using (var db = Context())
        {
            await db.Database.MigrateAsync(); var s = Services(db); var connection = await Connection(s.Api, "Http", local.Address, "None");
            var now = DateTime.UtcNow; var actor = Guid.NewGuid(); var definition = WorkflowDefinition.Create(_orgId, "sql-processor", "SQL processor", now);
            var version = WorkflowVersion.CreateDraft(definition.Id, 1, actor, now); version.SetWorkspace(Config(new WorkflowWorkspaceDefinition("Main", "C", "B", DesignerVersion: 4))); version.Publish(actor, now);
            var mapping = EmptyMapping() with
            {
                Outputs = [new("total", "Total", new("number"), new("source", Source: new(definition.Id, version.Id, "a", "Response", "$response", "/result")))],
                Actions = [new("call", new(definition.Id, version.Id, "b"), "Run", new("source", Source: new(definition.Id, version.Id, "a", "EventOutput", "total"))),
                    new("prepared", new(definition.Id, version.Id, "b"), "MapOnly", Literal(13))]
            };
            var child = EmptyMapping() with { Callable = true, InvocationInputs = new("number"), Inputs = [new("body", "$body", new("object", Properties: new() { ["text"] = new("text", Args: [new("source", Source: new(definition.Id, version.Id, "b", "InvocationInput", "$inputs"))]) }))] };
            var start = ActivityDefinition.Create(version.Id, "start", ActivityType.Start, "Start", now);
            var a = ActivityDefinition.Create(version.Id, "a", ActivityType.ServiceTask, "A", now, actionKey: "http.request", configurationJson: Config(new HttpActivityConfiguration { ConnectionId = connection, MappingMode = "Typed", TypedMapping = mapping }));
            var end = ActivityDefinition.Create(version.Id, "end", ActivityType.End, "End", now);
            var binding = WorkflowBinding.Create(definition.Id, _orgId, "Test", "Order", "Created", now, mode: WorkflowBindingMode.Active);
            db.AddRange(definition, version, binding, start, a, end,
                ActivityDefinition.Create(version.Id, "b", ActivityType.ServiceTask, "B", now, actionKey: "http.request", configurationJson: Config(new HttpActivityConfiguration { ConnectionId = connection, MappingMode = "Typed", TypedMapping = child })),
                WorkflowTransition.Create(version.Id, start.Id, a.Id, "enter", 1, now), WorkflowTransition.Create(version.Id, a.Id, end.Id, "exit", 1, now));
            await db.SaveChangesAsync(); var started = await s.Engine.StartAsync(_orgId, binding.Id, "order", "sql", now, startedByUserId: actor, pinnedWorkflowVersionId: version.Id);
            started.IsSuccess.Should().BeTrue(started.IsFailure ? started.Error.Message : ""); rootId = started.Value.Id;
            parentId = (await db.MappingInvocations.SingleAsync()).Id; jobId = (await db.IntegrationJobs.SingleAsync()).Id;
        }
        await using (var db = Context(true))
        {
            Func<Task> process = () => Services(db).Processor.ProcessJobAsync(jobId, default);
            await process.Should().ThrowAsync<IOException>(); fault.Fired.Should().BeTrue();
        }
        await using (var db = Context())
        {
            (await db.IntegrationJobs.SingleAsync()).Status.Should().Be("Delivered");
            var parent = await db.MappingInvocations.SingleAsync(); parent.Applied.Should().BeFalse(); parent.GraphAdvanced.Should().BeFalse();
            (await db.RoutingResults.CountAsync()).Should().Be(0); (await db.WorkflowInstances.FindAsync(rootId))!.Status.Should().Be(WorkflowInstanceStatus.Running);
        }
        async Task RecoverDelivery() { await using var db = Context(); await Services(db).Processor.ProcessJobAsync(jobId, default); }
        await Task.WhenAll(RecoverDelivery(), RecoverDelivery()).WaitAsync(TimeSpan.FromSeconds(30));
        await using (var db = Context())
        {
            var s = Services(db); await s.Runtime.RecoverAsync(default);
            var parent = await db.MappingInvocations.SingleAsync(i => i.Id == parentId); parent.Applied.Should().BeTrue(); parent.GraphAdvanced.Should().BeTrue();
            (await db.WorkflowInstances.FindAsync(rootId))!.Status.Should().Be(WorkflowInstanceStatus.Completed);
            var prepared = await db.MappingInvocations.SingleAsync(i => i.ParentInvocationId == parentId && i.ActionId == "prepared");
            prepared.ExecutionKind.Should().Be("MapOnly"); prepared.Applied.Should().BeTrue(); prepared.InvocationInputsJson.Should().Be("13");
            (await db.IntegrationJobs.AnyAsync(j => j.InvocationId == prepared.Id)).Should().BeFalse();
            var activities = await db.ActivityInstances.CountAsync(); var tokens = await db.WorkflowExecutionTokens.CountAsync();
            var child = await db.MappingInvocations.SingleAsync(i => i.ParentInvocationId == parentId && i.ActionId == "call");
            var job = await db.IntegrationJobs.SingleAsync(j => j.InvocationId == child.Id); await s.Processor.ProcessJobAsync(job.Id, default); await s.Processor.ProcessJobAsync(job.Id, default);
            await s.Processor.ProcessJobAsync(jobId, default); await s.Runtime.RecoverAsync(default);
            (await db.RoutingResults.CountAsync()).Should().Be(1); (await db.MappingInvocations.CountAsync()).Should().Be(3); (await db.IntegrationJobs.CountAsync()).Should().Be(2);
            (await db.ActivityInstances.CountAsync()).Should().Be(activities); (await db.WorkflowExecutionTokens.CountAsync()).Should().Be(tokens);
            local.Bodies.Should().Equal("{\"text\":\"{{literal}}\"}", "{\"text\":\"7\"}");
        }

    }
}
