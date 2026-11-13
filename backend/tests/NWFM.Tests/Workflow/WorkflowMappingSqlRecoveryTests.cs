namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.Integrations;
using global::Workflow.Application.Mapping;
using global::Workflow.Application.Settings;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Infrastructure.Persistence;
using global::Workflow.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using NWFM.Shared.Constants;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Organization;
using NWFM.Tests.Infrastructure;

public sealed partial class WorkflowRuntimeEnginePathTests
{
    [MappingSqlFact]
    public async Task MappingSqlRollbackAndConcurrentRecoveryCommitOutputsAndOneFollowUpWithoutResendingDelivery()
    {
        await using var sql = await OwnedLocalDb.CreateAsync();
        var connectionString = sql.ConnectionString;
        WorkflowDbContext Context() => new(new DbContextOptionsBuilder<WorkflowDbContext>().UseSqlServer(connectionString,
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "Workflow")).Options, new StubTenant(_orgId));
        using var local = new MappingLocalServer(); var protection = new EphemeralDataProtectionProvider();
        var access = new Mock<IWorkflowActorAccess>(); access.Setup(a => a.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new WorkflowActorAccess([NwfmPolicies.ViewInstances], OrgScopeSet.Unrestricted(), OrgHierarchy.Empty));
        var engine = new Mock<IWorkflowRuntimeEngine>(MockBehavior.Strict);
        (WorkflowMappingRuntime Runtime, WorkflowIntegrations Api, WorkflowIntegrationProcessor Processor) Services(WorkflowDbContext db)
        {
            var runtime = new WorkflowMappingRuntime(db, new MappingSourceResolver(db, access.Object, Mock.Of<IFormGateway>()), Mock.Of<IServiceProvider>());
            var api = new WorkflowIntegrations(db, new StubTenant(_orgId), protection, new WorkflowIntegrationTransport(), Options.Create(new WorkflowSettings { AllowPrivateConnections = true }), runtime);
            return (runtime, api, new WorkflowIntegrationProcessor(db, api, new WorkflowIntegrationTransport(), engine.Object, mappings: runtime));
        }
        Guid rootId, parentId, jobId;
        var delivery = new IntegrationResult(true, 200, "{\"result\":7}", null);
        await using (var db = Context())
        {
            await db.Database.MigrateAsync(); var services = Services(db); var connection = await Connection(services.Api, "Http", local.Address, "None");
            var now = DateTime.UtcNow; var actor = Guid.NewGuid(); var definition = WorkflowDefinition.Create(_orgId, "sql-recovery", "SQL recovery", now);
            var version = WorkflowVersion.CreateDraft(definition.Id, 1, actor, now); version.SetWorkspace(Config(new WorkflowWorkspaceDefinition("Main", "C", "B", DesignerVersion: 4)));
            var response = new SourceReference(definition.Id, version.Id, "a", "Response", "$response", "/result");
            var mapping = EmptyMapping() with
            {
                Outputs = [new("total", "Total", new("number"), new("source", Source: response))],
                Actions = [new("stable-action", new(definition.Id, version.Id, "b"), "Run", new("source", Source: new(definition.Id, version.Id, "a", "EventOutput", "total")))]
            };
            var config = Config(new HttpActivityConfiguration { ConnectionId = connection, MappingMode = "Typed", TypedMapping = mapping });
            var targetMapping = EmptyMapping() with { Callable = true, InvocationInputs = new("number"), Inputs = [new("body", "$body", new("object", Properties: new() { ["text"] = new("text", Args: [new("source", Source: new(definition.Id, version.Id, "b", "InvocationInput", "$inputs"))]) }))] };
            var root = WorkflowInstance.Start(_orgId, Guid.NewGuid(), version.Id, "root", "key", "a", now, startedByUserId: actor);
            var origin = ActivityInstance.Start(_orgId, root.Id, "a", ActivityType.ServiceTask, "A", now);
            var parent = WorkflowMappingInvocation.Create(_orgId, root.Id, origin.Id, null, actor, root.Id, version.Id, "a", "Callable", config, "parent", now);
            var job = WorkflowIntegrationJob.Create(_orgId, root.Id, origin.Id, connection, "Http", config, "{}", "parent"); job.AttachInvocation(parent.Id, "Callable");
            // The external response is durably recorded before either completion worker starts.
            job.RecordDelivery(Config(delivery), 200);
            db.AddRange(definition, version, root, origin, parent, job,
                ActivityDefinition.Create(version.Id, "a", ActivityType.ServiceTask, "A", now, actionKey: "http.request", configurationJson: config),
                ActivityDefinition.Create(version.Id, "b", ActivityType.ServiceTask, "B", now, actionKey: "http.request", configurationJson: Config(new HttpActivityConfiguration { ConnectionId = connection, MappingMode = "Typed", TypedMapping = targetMapping })));
            await db.SaveChangesAsync(); rootId = root.Id; parentId = parent.Id; jobId = job.Id;
        }
        await using (var db = Context())
        {
            var runtime = Services(db).Runtime; var job = await db.IntegrationJobs.SingleAsync(j => j.Id == jobId);
            Func<Task> crash = async () => await WorkflowExecutionLock.RunAsync<bool>(db, "instance:" + rootId, async () =>
            {
                await runtime.ApplyDeliveryAsync(job, delivery, engine.Object, default);
                throw new IOException("Simulated process loss before the owning root transaction commits.");
            }, default);
            await crash.Should().ThrowAsync<IOException>();
        }
        await using (var db = Context())
        {
            var parent = await db.MappingInvocations.SingleAsync(i => i.Id == parentId);
            parent.Applied.Should().BeFalse(); parent.OutputsJson.Should().BeNull();
            (await db.MappingInvocations.CountAsync(i => i.ParentInvocationId == parentId)).Should().Be(0);
            (await db.IntegrationJobs.SingleAsync(j => j.Id == jobId)).Status.Should().Be("Delivered");
        }
        var arrived = 0; var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task CompleteConcurrently()
        {
            await using var db = Context(); var runtime = Services(db).Runtime;
            await db.MappingInvocations.SingleAsync(i => i.Id == parentId); var job = await db.IntegrationJobs.SingleAsync(j => j.Id == jobId);
            if (Interlocked.Increment(ref arrived) == 2) ready.SetResult(); await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await WorkflowExecutionLock.RunAsync(db, "instance:" + rootId, async () =>
            {
                await db.Entry(job).ReloadAsync(); await runtime.ApplyDeliveryAsync(job, delivery, engine.Object, default); return true;
            }, default);
        }
        await Task.WhenAll(CompleteConcurrently(), CompleteConcurrently()).WaitAsync(TimeSpan.FromSeconds(30));
        async Task Recover()
        { await using var db = Context(); await Services(db).Runtime.RecoverAsync(default); }
        await Task.WhenAll(Recover(), Recover()).WaitAsync(TimeSpan.FromSeconds(30));
        await using (var db = Context())
        {
            var parent = await db.MappingInvocations.SingleAsync(i => i.Id == parentId); parent.Applied.Should().BeTrue(); parent.OutputsJson.Should().Contain("7");
            var child = await db.MappingInvocations.SingleAsync(i => i.ParentInvocationId == parentId);
            child.ActionId.Should().Be("stable-action"); child.InvocationInputsJson.Should().Be("7");
            var job = await db.IntegrationJobs.SingleAsync(j => j.InvocationId == child.Id); job.OperationKey.Should().Be(parent.Id + ":stable-action");
            var processor = Services(db).Processor; await processor.ProcessJobAsync(job.Id, default); await processor.ProcessJobAsync(job.Id, default);
            (await db.IntegrationJobs.CountAsync()).Should().Be(2); (await db.MappingInvocations.CountAsync()).Should().Be(2);
            child.Applied.Should().BeTrue(); local.Bodies.Should().Equal("{\"text\":\"7\"}");
        }
        engine.VerifyNoOtherCalls();

    }
}
