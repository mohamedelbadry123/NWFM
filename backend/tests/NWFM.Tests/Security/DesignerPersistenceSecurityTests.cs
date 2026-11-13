using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Exceptions;
using NWFM.Tests.Infrastructure;
using NWFM.Tests.Modules.Workflow;
using Workflow.Application.Settings;
using Workflow.Domain.Entities;
using Workflow.Infrastructure.Persistence;
using Workflow.Infrastructure.Services;

namespace NWFM.Tests.Security;

public sealed class DesignerPersistenceSecurityTests
{
    private sealed record Tenant(Guid OrganizationId) : ICurrentTenant;

    private sealed class OverlappingAdmission : DbCommandInterceptor
    {
        private int attempts;
        private readonly TaskCompletionSource secondAttempt = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private DbCommand? first;
        public bool ObservedOverlap { get; private set; }
        private static bool IsCapacity(DbCommand command) => command.CommandText.Contains("sp_getapplock", StringComparison.Ordinal)
            && command.Parameters.Cast<DbParameter>().Any(p => p.Value is string s && s.StartsWith("nwfm:queue-capacity:", StringComparison.Ordinal));
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (IsCapacity(command))
            {
                if (Interlocked.Increment(ref attempts) == 1) first = command;
                else secondAttempt.TrySetResult();
            }
            return ValueTask.FromResult(result);
        }
        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(command, first))
            {
                // First transaction owns the tenant lock while the second attempts acquisition.
                await secondAttempt.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
                ObservedOverlap = true;
            }
            return result;
        }
    }

    private sealed class FailAfterReservationsReachSql : DbTransactionInterceptor
    {
        public bool Fired { get; private set; }
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Fired) return result;
            await using var command = transaction.Connection!.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT COUNT(*) FROM [Workflow].[form_receipt_reservations]";
            Assert.Equal(2, Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)));
            Fired = true; throw new IOException("Injected commit failure after reservation and invocation inserts.");
        }
    }

    [MappingSqlFact]
    public async Task QueueAdmissionAndReceiptReservationCommitAtomicallyAndNewRowsRetainTenantConcurrency()
    {
        const string tenantOwnershipMigration = "20261020164057_TenantOwnershipConcurrency";
        await using var sql = await OwnedLocalDb.CreateAsync();
        var tenant = Guid.NewGuid();
        var overlap = new OverlappingAdmission();
        WorkflowDbContext Context(Guid? organization = null) => new(new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseSqlServer(sql.ConnectionString, o => o.MigrationsHistoryTable("__EFMigrationsHistory", "Workflow")).AddInterceptors(overlap).Options,
            new Tenant(organization ?? tenant), Options.Create(new WorkflowSettings { MaxPendingPerQueue = 1 }));
        var legacyId = Guid.NewGuid(); var legacyActor = Guid.NewGuid();
        var roots = new[] { WorkflowInstance.Start(tenant, Guid.NewGuid(), Guid.NewGuid(), "root-a", "a", "start", DateTime.UtcNow),
            WorkflowInstance.Start(tenant, Guid.NewGuid(), Guid.NewGuid(), "root-b", "b", "start", DateTime.UtcNow) };
        await using (var db = Context())
        {
            await db.GetService<IMigrator>().MigrateAsync(tenantOwnershipMigration);
            // Write an actual pre-designer claimed work item through its old schema, not the current model.
            var instance = Guid.NewGuid(); var activity = Guid.NewGuid(); var group = Guid.NewGuid(); var now = DateTime.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [Workflow].[work_items] ([Id],[OrganizationId],[WorkflowInstanceId],[ActivityInstanceId],[AssignmentGroupId],[Status],[ClaimedByUserId],[ClaimedAt],[CreatedAt],[UpdatedAt]) VALUES ({legacyId},{tenant},{instance},{activity},{group},N'Claimed',{legacyActor},{now},{now},{now})");
            await db.Database.MigrateAsync();
            var legacy = await db.WorkItems.SingleAsync(x => x.Id == legacyId);
            Assert.Equal(legacyActor, legacy.ClaimedByUserId); Assert.Null(legacy.FormReceiptAttempt);
            Assert.True(await db.FormReceiptReservations.AnyAsync(r => r.ReceiptKey == legacyId && r.WorkItemId == legacyId));
            db.AddRange(roots); await db.SaveChangesAsync();
            Assert.False(db.Database.HasPendingModelChanges());
            var migrations = db.GetService<IMigrationsAssembly>();
            foreach (var entry in migrations.Migrations.Where(m => string.CompareOrdinal(m.Key, tenantOwnershipMigration) > 0))
            {
                var model = migrations.CreateMigration(entry.Value, db.Database.ProviderName!).TargetModel;
                foreach (var entity in model.GetEntityTypes().Where(e => e.FindProperty("OrganizationId")?.ClrType == typeof(Guid)))
                    Assert.True(entity.FindProperty("OrganizationId")!.IsConcurrencyToken, entry.Key + ":" + entity.Name);
            }
        }
        var rootCount = 0; var bothRootsHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Admit(WorkflowInstance root)
        {
            await using var db = Context();
            var item = WorkItem.Create(tenant, root.Id, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
            item.Claim(Guid.NewGuid(), DateTime.UtcNow);
            var call = WorkflowMappingInvocation.Create(tenant, Guid.NewGuid(), item.ActivityInstanceId, null,
                item.ClaimedByUserId!.Value, Guid.NewGuid(), Guid.NewGuid(), "call", "Callable", "{}", item.Id.ToString(), DateTime.UtcNow);
            try
            {
                return await WorkflowExecutionLock.RunAsync(db, "instance:" + root.Id, async () =>
                {
                    if (Interlocked.Increment(ref rootCount) == 2) bothRootsHeld.TrySetResult();
                    await bothRootsHeld.Task.WaitAsync(TimeSpan.FromSeconds(20));
                    db.AddRange(item, call); await db.SaveChangesAsync(); return true;
                }, default);
            }
            catch (WorkflowQueueCapacityException) { return false; }
        }
        var admitted = await Task.WhenAll(Admit(roots[0]), Admit(roots[1])).WaitAsync(TimeSpan.FromSeconds(45));
        Assert.True(overlap.ObservedOverlap); Assert.Equal(2, rootCount);
        Assert.Single(admitted, x => x); Assert.Single(admitted, x => !x);
        Guid invocationId;
        await using (var db = Context())
        {
            var item = await db.WorkItems.SingleAsync(x => x.Id != legacyId); var call = await db.MappingInvocations.SingleAsync(); invocationId = call.Id;
            var receipt = item.FormReceiptKey(item.Id, item.ClaimedByUserId!.Value);
            Assert.Equal(3, await db.FormReceiptReservations.CountAsync());
            Assert.True(await db.FormReceiptReservations.AnyAsync(r => r.ReceiptKey == receipt && r.WorkItemId == item.Id));
        }
        var foreignTenant = Guid.NewGuid();
        await using (var foreign = Context(foreignTenant))
        {
            // Give the attacker the real row version as well: ownership alone must reject the write.
            var forged = await foreign.MappingInvocations.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == invocationId);
            var entry = foreign.Entry(forged);
            entry.Property(x => x.OrganizationId).CurrentValue = foreignTenant;
            entry.State = EntityState.Unchanged;
            entry.Property(x => x.OrganizationId).OriginalValue = foreignTenant;
            entry.Property(x => x.Error).CurrentValue = "foreign edit"; entry.Property(x => x.Error).IsModified = true;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => foreign.SaveChangesAsync());
        }
        await using (var db = Context())
        {
            var completed = await db.MappingInvocations.SingleAsync(); Assert.Null(completed.Error);
            completed.Complete("{}", DateTime.UtcNow); await db.SaveChangesAsync();
        }
        Assert.True(await Admit(roots[Array.FindIndex(admitted, success => !success)]).WaitAsync(TimeSpan.FromSeconds(30)));
        await using (var db = Context()) Assert.Equal(2, await db.MappingInvocations.CountAsync());
    }

    [MappingSqlFact]
    public async Task CommitFailureAfterReceiptPreparationRollsBackAndRetryKeepsTheSameReservation()
    {
        await using var sql = await OwnedLocalDb.CreateAsync(); var tenant = Guid.NewGuid(); var fault = new FailAfterReservationsReachSql();
        WorkflowDbContext Context(bool inject = false)
        {
            var builder = new DbContextOptionsBuilder<WorkflowDbContext>().UseSqlServer(sql.ConnectionString, o => o.MigrationsHistoryTable("__EFMigrationsHistory", "Workflow"));
            if (inject) builder.AddInterceptors(fault);
            return new(builder.Options, new Tenant(tenant), Options.Create(new WorkflowSettings { MaxPendingPerQueue = 1 }));
        }
        await using (var db = Context()) await db.Database.MigrateAsync();
        var item = WorkItem.Create(tenant, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow); var actor = Guid.NewGuid(); item.Claim(actor, DateTime.UtcNow);
        var receipt = item.FormReceiptKey(item.Id, actor);
        var invocation = WorkflowMappingInvocation.Create(tenant, item.WorkflowInstanceId, item.ActivityInstanceId, null, actor,
            item.WorkflowInstanceId, Guid.NewGuid(), "event", "Callable", "{}", "atomic", DateTime.UtcNow);
        await using var writer = Context(true); writer.AddRange(item, invocation);
        await Assert.ThrowsAsync<IOException>(() => writer.SaveChangesAsync()); Assert.True(fault.Fired);
        await using (var check = Context())
        { Assert.Empty(await check.WorkItems.ToListAsync()); Assert.Empty(await check.MappingInvocations.ToListAsync()); Assert.Empty(await check.FormReceiptReservations.ToListAsync()); }
        // The failed commit accepted tracked values before throwing; discard that EF state before retry.
        writer.ChangeTracker.Clear(); writer.AddRange(item, invocation); await writer.SaveChangesAsync();
        await using (var check = Context())
        {
            Assert.Single(await check.WorkItems.ToListAsync()); Assert.Single(await check.MappingInvocations.ToListAsync());
            Assert.Equal(2, await check.FormReceiptReservations.CountAsync());
            Assert.True(await check.FormReceiptReservations.AnyAsync(r => r.ReceiptKey == receipt && r.WorkItemId == item.Id));
        }
    }

    [MappingSqlFact]
    public async Task InvertedRootAndCapacityLockCycleIsBoundedAndWholeTransactionRetrySucceeds()
    {
        await using var sql = await OwnedLocalDb.CreateAsync(); var tenant = Guid.NewGuid();
        WorkflowDbContext Context() => new(new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseSqlServer(sql.ConnectionString, o => o.MigrationsHistoryTable("__EFMigrationsHistory", "Workflow")).Options,
            new Tenant(tenant), Options.Create(new WorkflowSettings { MaxPendingPerQueue = 2 }));
        var roots = new[] { WorkflowInstance.Start(tenant, Guid.NewGuid(), Guid.NewGuid(), "cycle-a", "a", "start", DateTime.UtcNow),
            WorkflowInstance.Start(tenant, Guid.NewGuid(), Guid.NewGuid(), "cycle-b", "b", "start", DateTime.UtcNow) };
        await using (var db = Context()) { await db.Database.MigrateAsync(); db.AddRange(roots); await db.SaveChangesAsync(); }
        var bothRoots = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capacityHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var acquiredRoots = 0; var retries = 0;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        async Task Run(int index)
        {
            // Stable invocation identity survives transaction rollback; only the failed unit is retried.
            var call = WorkflowMappingInvocation.Create(tenant, roots[index].Id, Guid.NewGuid(), null, Guid.NewGuid(), roots[index].Id,
                Guid.NewGuid(), "event", "Callable", "{}", "cycle-" + index, DateTime.UtcNow);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                await using var db = Context();
                try
                {
                    await WorkflowExecutionLock.RunAsync(db, "instance:" + roots[index].Id, async () =>
                    {
                        if (attempt == 0)
                        {
                            if (Interlocked.Increment(ref acquiredRoots) == 2) bothRoots.TrySetResult();
                            await bothRoots.Task.WaitAsync(deadline.Token);
                            if (index == 1) await capacityHeld.Task.WaitAsync(deadline.Token);
                        }
                        db.Add(call); await db.SaveChangesAsync(deadline.Token);
                        if (attempt == 0 && index == 0)
                        {
                            capacityHeld.TrySetResult();
                            // Deliberately invert the supported root->capacity order to exercise SQL's
                            // deadlock/app-lock timeout path, rather than simulate a transient exception.
                            await WorkflowExecutionLock.RunAsync(db, "instance:" + roots[1].Id, () => Task.FromResult(true), deadline.Token);
                        }
                        return true;
                    }, deadline.Token);
                    return;
                }
                catch (Exception ex) when (attempt == 0 && WorkflowExecutionLock.IsTransient(db, ex))
                { Interlocked.Increment(ref retries); }
            }
            throw new InvalidOperationException("Whole transaction retry did not complete.");
        }
        await Task.WhenAll(Run(0), Run(1)).WaitAsync(TimeSpan.FromSeconds(55));
        Assert.InRange(retries, 1, 2);
        await using (var db = Context())
        {
            Assert.Equal(2, await db.MappingInvocations.CountAsync());
            Assert.Equal(2, await db.MappingInvocations.Select(i => i.OperationKey).Distinct().CountAsync());
        }
    }
}
