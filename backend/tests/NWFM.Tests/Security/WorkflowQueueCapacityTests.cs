using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Exceptions;
using Workflow.Application.Settings;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Infrastructure.Persistence;

namespace NWFM.Tests.Security;

public sealed class WorkflowQueueCapacityTests
{
    private sealed record Tenant(Guid OrganizationId) : ICurrentTenant;

    private static WorkflowDbContext Context(Guid tenant, string database) => new(
        new DbContextOptionsBuilder<WorkflowDbContext>().UseInMemoryDatabase(database).Options,
        new Tenant(tenant), Options.Create(new WorkflowSettings { MaxPendingPerQueue = 1 }));

    private static object Message(string queue, Guid tenant) => queue switch
    {
        "inbox" => WorkflowIntegrationInbox.Create(tenant, Guid.NewGuid().ToString(), "idempotency", "module",
            "entity", "1", "trigger", "{}", DateTime.UtcNow),
        "outbox" => WorkflowIntegrationOutbox.Create(tenant, Guid.NewGuid().ToString(), Guid.NewGuid(),
            Guid.NewGuid(), "module", "entity", "1", "done", "{}", DateTime.UtcNow),
        "jobs" => WorkflowIntegrationJob.Create(tenant, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Http", "{}", "{}", Guid.NewGuid().ToString()),
        "receipts" => WorkflowEventReceipt.Create(tenant, Guid.NewGuid(), Guid.NewGuid().ToString(), "event", "correlation", "{}"),
        "waits" => WorkflowEventSubscription.Create(tenant, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "event", "correlation", "{}", DateTime.UtcNow.AddMinutes(1)),
        "timers" => WorkflowTimer.Create(tenant, Guid.NewGuid(), Guid.NewGuid(), WorkflowTimerType.DueDate, DateTime.UtcNow.AddMinutes(1), DateTime.UtcNow),
        _ => throw new ArgumentOutOfRangeException(nameof(queue)),
    };

    private static object TerminalStatus(string queue) => queue switch
    {
        "inbox" => WorkflowInboxStatus.Processed,
        "outbox" => WorkflowOutboxStatus.Published,
        "timers" => WorkflowTimerStatus.Completed,
        _ => "Completed",
    };

    [Theory]
    [InlineData("inbox")]
    [InlineData("outbox")]
    [InlineData("jobs")]
    [InlineData("receipts")]
    [InlineData("waits")]
    [InlineData("timers")]
    public async Task EveryPersistedQueueRejectsOverflowAndAllowsTerminalReplacement(string queue)
    {
        var tenant = Guid.NewGuid();
        using var db = Context(tenant, Guid.NewGuid().ToString());
        var first = Message(queue, tenant);
        db.Add(first);
        await db.SaveChangesAsync();

        // Ordinary active-state changes do not consume an additional queue slot.
        db.Entry(first).Property("UpdatedAt").CurrentValue = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var second = Message(queue, tenant);
        db.Add(second);
        await Assert.ThrowsAsync<WorkflowQueueCapacityException>(() => db.SaveChangesAsync());

        // Completing the first and admitting its replacement atomically has zero net growth.
        db.Entry(first).Property("Status").CurrentValue = TerminalStatus(queue);
        await db.SaveChangesAsync();
        Assert.Equal(EntityState.Unchanged, db.Entry(second).State);
    }

    [Fact]
    public async Task ReplayCannotBypassCapacityAndCanResumeAfterSpaceIsReleased()
    {
        var tenant = Guid.NewGuid();
        using var db = Context(tenant, Guid.NewGuid().ToString());
        var failed = (WorkflowIntegrationJob)Message("jobs", tenant);
        failed.Fail("temporary failure", 503, null);
        var active = (WorkflowIntegrationJob)Message("jobs", tenant);
        db.AddRange(failed, active);
        await db.SaveChangesAsync();

        failed.Replay();
        Assert.Throws<WorkflowQueueCapacityException>(() => db.SaveChanges());
        active.Complete("{}", 200);
        db.SaveChanges();
        Assert.Equal("Pending", failed.Status);
    }

    [Fact]
    public async Task AnotherTenantCannotConsumeTheCurrentTenantsBudget()
    {
        var database = Guid.NewGuid().ToString();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        using var first = Context(a, database);
        first.Add(Message("jobs", a));
        await first.SaveChangesAsync();
        using var second = Context(b, database);
        second.Add(Message("jobs", b));
        await second.SaveChangesAsync();
        Assert.Single(await second.IntegrationJobs.ToListAsync());
    }
}
