namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Domain.Entities;
using global::Workflow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NWFM.Tests.Infrastructure;

public sealed class MappingSqlFactAttribute : FactAttribute
{
    public MappingSqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("NWFM_MAPPING_SQL_TESTS") != "1") Skip = "Opt in to the isolated LocalDB migration test with NWFM_MAPPING_SQL_TESTS=1.";
    }
}

public sealed partial class WorkflowRuntimeEnginePathTests
{
    [MappingSqlFact]
    public async Task TypedMappingSqlUpgradePreservesLegacyAndDownRemovesOnlyAdditiveSchema()
    {
        await using var sql = await OwnedLocalDb.CreateAsync();
        var connection = sql.ConnectionString;
        WorkflowDbContext Context() => new(new DbContextOptionsBuilder<WorkflowDbContext>().UseSqlServer(connection,
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "Workflow")).Options, new StubTenant(_orgId));
        var legacyId = Guid.NewGuid(); var legacyInvocation = Guid.NewGuid(); var now = DateTime.UtcNow;
        await using (var db = Context())
        {
            await db.GetService<IMigrator>().MigrateAsync("20261029105540_RoutingCoordination");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT [Workflow].[integration_jobs] ([Id],[OrganizationId],[WorkflowInstanceId],[ActivityInstanceId],[ConnectionId],[Kind],[ConfigurationJson],[InputJson],[OperationKey],[Status],[Attempts],[NextAttemptAt],[IsActivityEvent],[Required],[CreatedAt],[UpdatedAt]) VALUES ({legacyId},{_orgId},{Guid.NewGuid()},{Guid.NewGuid()},{Guid.NewGuid()},N'Http',N'{{}}',N'{{}}',N'legacy',N'Pending',0,{now},0,0,{now},{now})");
            await db.GetService<IMigrator>().MigrateAsync("20261110183804_AddWorkflowMappingInvocations");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT [Workflow].[mapping_invocations] ([Id],[OrganizationId],[RootInstanceId],[OriginActivityId],[ActorUserId],[TargetInstanceId],[TargetVersionId],[TargetNodeKey],[ExecutionKind],[ActionId],[OperationKey],[ConfigurationJson],[Status],[WindowAnchor],[Deadline],[CriteriaJson],[SelectionsJson],[SourcesJson],[Applied],[GraphAdvanced],[CreatedAt],[UpdatedAt]) VALUES ({legacyInvocation},{_orgId},{Guid.NewGuid()},{Guid.NewGuid()},{Guid.NewGuid()},{Guid.NewGuid()},{Guid.NewGuid()},N'event',N'Callable',N'',N'old-wait',N'{{}}',N'Waiting',{now},{now.AddHours(48)},N'[]',N'{{}}',N'[]',0,0,{now},{now})");
            var evidence = new[] { ("Completed", "InstanceCompleted", true), ("Cancelled", "InstanceCancelled", true), ("Suspended", "InstanceSuspended", true),
                ("Running", "InstanceResumed", true), ("Failed", "InstanceFailed", true), ("Running", "InstanceSuspended", false), ("Failed", "InstanceStarted", false), ("Running", "InstanceResumed", false) };
            var expected = new Dictionary<Guid, DateTime?>();
            foreach (var (status, eventType, known) in evidence)
            {
                var id = Guid.NewGuid(); var entered = now.AddSeconds(5); expected[id] = known ? entered : null;
                var updated = known && status is "Running" or "Failed" ? entered : now.AddSeconds(20);
                DateTime? completed = status == "Completed" ? entered : null, cancelled = status == "Cancelled" ? entered : null, suspended = status == "Suspended" ? entered : null;
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT [Workflow].[workflow_instances] ([Id],[OrganizationId],[WorkflowBindingId],[PinnedWorkflowVersionId],[IdempotencyKey],[BusinessEntityId],[Status],[StartedAt],[CompletedAt],[CancelledAt],[SuspendedAt],[CurrentActivityNodeKey],[CreatedAt],[UpdatedAt],[IsDemo]) VALUES ({id},{_orgId},{Guid.NewGuid()},{Guid.NewGuid()},{id.ToString()},N'legacy',{status},{now},{completed},{cancelled},{suspended},N'activity',{now},{updated},0)");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT [Workflow].[workflow_events] ([Id],[OrganizationId],[WorkflowInstanceId],[EventType],[OccurredAt]) VALUES ({Guid.NewGuid()},{_orgId},{id},{eventType},{entered})");
            }
            await db.Database.MigrateAsync();
            var migratedInstances = await db.WorkflowInstances.AsNoTracking().ToListAsync(); migratedInstances.Should().HaveCount(expected.Count);
            foreach (var instance in migratedInstances) instance.StatusEnteredAt.Should().Be(expected[instance.Id]);
            // Repeated status calls and unrelated progress preserve the current interval; a real resume starts a new one.
            var resumed = await db.WorkflowInstances.FirstAsync(i => i.Status == global::Workflow.Domain.Enums.WorkflowInstanceStatus.Running && i.StatusEnteredAt != null);
            resumed.Resume(now.AddSeconds(30)); await db.SaveChangesAsync(); resumed.StatusEnteredAt.Should().Be(now.AddSeconds(5));
            resumed.Suspend(now.AddSeconds(40)); resumed.Resume(now.AddSeconds(50)); await db.SaveChangesAsync();
            await db.Entry(resumed).ReloadAsync(); resumed.StatusEnteredAt.Should().Be(now.AddSeconds(50));
            var upgraded = await db.MappingInvocations.SingleAsync(i => i.Id == legacyInvocation);
            upgraded.Status.Should().Be("Waiting"); upgraded.WaitDependenciesJson.Should().Be("[]"); upgraded.Deadline.Should().Be(now.AddHours(48));
            var legacy = await db.IntegrationJobs.SingleAsync(); legacy.InvocationId.Should().BeNull(); legacy.ExecutionKind.Should().Be("Legacy"); legacy.OperationKey.Should().Be("legacy");
            var parent = Guid.NewGuid(); var root = Guid.NewGuid();
            WorkflowMappingInvocation Invocation(string operation) => WorkflowMappingInvocation.Create(_orgId, root, Guid.NewGuid(), null, Guid.NewGuid(), root, Guid.NewGuid(), "event", "Callable", "{}", operation, now, parent, "action");
            db.MappingInvocations.Add(Invocation("first")); await db.SaveChangesAsync();
            db.MappingInvocations.Add(Invocation("duplicate"));
            Func<Task> duplicate = () => db.SaveChangesAsync(); await duplicate.Should().ThrowAsync<DbUpdateException>();
        }
        await using (var db = Context())
        {
            (await db.MappingInvocations.CountAsync()).Should().Be(2);
            await db.GetService<IMigrator>().MigrateAsync("20261029105540_RoutingCoordination");
            await db.Database.MigrateAsync();
            (await db.IntegrationJobs.SingleAsync()).Id.Should().Be(legacyId); (await db.MappingInvocations.CountAsync()).Should().Be(0);
        }

    }
}
