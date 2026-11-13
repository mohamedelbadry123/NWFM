namespace NWFM.Tests.Modules.Workflow;

using System.Text.Json;
using FluentAssertions;
using global::Workflow.Application.Integrations;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Infrastructure.Persistence;
using global::Workflow.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using NWFM.Tests.Infrastructure;

public sealed class RoutingSqlFactAttribute : FactAttribute
{
    public RoutingSqlFactAttribute() { if (Environment.GetEnvironmentVariable("NWFM_ROUTING_SQL_TESTS") != "1") Skip = "Set NWFM_ROUTING_SQL_TESTS=1 to run against a newly created disposable local SQL database."; }
}

public sealed partial class WorkflowRuntimeEnginePathTests
{
    [RoutingSqlFact]
    public async Task Routing_Sql_MigrationsAndSimultaneousCompletionsReleaseExactlyOnce()
    {
        RoutingAssignments();
        await using var sql = await OwnedLocalDb.CreateAsync();
        var connectionString = sql.ConnectionString;
        WorkflowDbContext Context() => new(new DbContextOptionsBuilder<WorkflowDbContext>().UseSqlServer(connectionString,
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "Workflow")).Options, new StubTenant(_orgId));
        await using (var setup = Context()) await setup.Database.MigrateAsync();
        foreach (var policy in new[] { "Continue", "Cancel" })
        {
            Guid instanceId;
            await using (var setup = Context())
            {
                var now = DateTime.UtcNow; var graph = WorkflowRoutingTests.Parallel("Any", policy);
                var definition = WorkflowDefinition.Create(_orgId, "routing-" + policy, "Routing SQL", now); setup.WorkflowDefinitions.Add(definition);
                var version = WorkflowVersion.CreateDraft(definition.Id, 1, Guid.NewGuid(), now);
                version.SetWorkspace("{\"designerVersion\":4,\"kind\":\"Main\",\"clusterCode\":\"C\",\"cbuCode\":\"B\"}"); version.Publish(Guid.NewGuid(), now); setup.WorkflowVersions.Add(version);
                var nodes = graph.Nodes.Select(n => ActivityDefinition.Create(version.Id, n.Key, n.Type == "WaitEvent" ? ActivityType.UserTask : Enum.Parse<ActivityType>(n.Type), n.Name, now,
                    configurationJson: n.Routing is null ? "{}" : JsonSerializer.Serialize(new { routing = n.Routing }, IntegrationJson.Options))).ToDictionary(n => n.NodeKey);
                setup.ActivityDefinitions.AddRange(nodes.Values);
                setup.WorkflowTransitions.AddRange(graph.Connections.Select(e => WorkflowTransition.Create(version.Id, nodes[e.From].Id, nodes[e.To].Id, e.Key, 0, now)));
                var binding = WorkflowBinding.Create(definition.Id, _orgId, "Test", "Routing" + policy, "Created", now, mode: WorkflowBindingMode.Active); setup.WorkflowBindings.Add(binding);
                await setup.SaveChangesAsync();
                var started = await WorkflowExecutionLock.RunAsync(setup, "start:" + binding.Id, () => BuildEngine(setup).StartAsync(_orgId, binding.Id, "test", Guid.NewGuid().ToString(), now, pinnedWorkflowVersionId: version.Id), default);
                started.IsSuccess.Should().BeTrue(started.IsFailure ? started.Error.Message : ""); instanceId = started.Value.Id;
            }
            async Task Complete(string key)
            {
                await using var db = Context();
                await WorkflowExecutionLock.RunAsync(db, "instance:" + instanceId, async () =>
                {
                    var visit = await db.ActivityInstances.SingleAsync(a => a.WorkflowInstanceId == instanceId && a.ActivityNodeKey == key);
                    var item = await db.WorkItems.SingleAsync(w => w.ActivityInstanceId == visit.Id);
                    if (item.Status == WorkItemStatus.Cancelled) return true;
                    if (item.Status != WorkItemStatus.Completed)
                    {
                        var actor = Guid.NewGuid(); item.Claim(actor, DateTime.UtcNow); item.SetFormData("{\"approved\":true}"); item.Complete(actor, "APPROVE", DateTime.UtcNow); await db.SaveChangesAsync();
                    }
                    var result = await BuildEngine(db).AdvanceAsync(instanceId, item.Id, DateTime.UtcNow);
                    result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : ""); return true;
                }, default);
            }
            await Task.WhenAll(Complete("one"), Complete("two"), Complete("event16"), Complete("one"));
            await using var verify = Context();
            (await verify.ActivityInstances.CountAsync(a => a.WorkflowInstanceId == instanceId && a.ActivityNodeKey == "next")).Should().Be(1);
            (await verify.RoutingActivations.SingleAsync(g => g.WorkflowInstanceId == instanceId)).ReleasedAt.Should().NotBeNull();
            // A fresh context after every callback also verifies persistence across engine restarts.
            var hidden = new WorkflowDbContext(new DbContextOptionsBuilder<WorkflowDbContext>().UseSqlServer(connectionString).Options, new StubTenant(Guid.NewGuid()));
            await using (hidden) (await hidden.RoutingActivations.CountAsync()).Should().Be(0);
        }
    }
}
