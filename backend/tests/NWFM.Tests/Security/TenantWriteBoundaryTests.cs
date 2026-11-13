using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Abstractions;
using Workflow.Domain.Entities;
using Workflow.Infrastructure.Persistence;

namespace NWFM.Tests.Security;

public sealed class TenantWriteBoundaryTests
{
    private sealed record Tenant(Guid OrganizationId) : ICurrentTenant;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ATrackedForeignRecordCannotBeReassignedToTheCurrentTenant(bool synchronous)
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using (var seed = new WorkflowDbContext(options, new Tenant(b)))
        {
            seed.Add(WorkflowDefinition.Create(b, "FOREIGN", "Foreign", DateTime.UtcNow));
            await seed.SaveChangesAsync();
        }

        using var db = new WorkflowDbContext(options, new Tenant(a));
        // Simulate an accidental privileged load followed by an ownership rewrite.
        var foreign = await db.WorkflowDefinitions.IgnoreQueryFilters().SingleAsync();
        db.Entry(foreign).Property(x => x.OrganizationId).CurrentValue = a;
        if (synchronous) Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        else await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DetachedObjectCannotUpdateOrDeleteAnotherTenantsPrimaryKey(bool delete)
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var foreign = WorkflowDefinition.Create(b, "FOREIGN", "Foreign", DateTime.UtcNow);
        using (var seed = new WorkflowDbContext(options, new Tenant(b)))
        {
            seed.Add(foreign);
            await seed.SaveChangesAsync();
        }

        using var db = new WorkflowDbContext(options, new Tenant(a));
        var forged = WorkflowDefinition.Create(a, "FORGED", "Forged", DateTime.UtcNow);
        db.Entry(forged).Property(x => x.Id).CurrentValue = foreign.Id;
        db.Entry(forged).State = delete ? EntityState.Deleted : EntityState.Modified;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());

        using var check = new WorkflowDbContext(options, new Tenant(b));
        Assert.Equal("Foreign", (await check.WorkflowDefinitions.SingleAsync()).Name);
    }

    [Fact]
    public async Task SameTenantUpdateAndDeleteContinueToWork()
    {
        var tenant = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var db = new WorkflowDbContext(options, new Tenant(tenant));
        var definition = WorkflowDefinition.Create(tenant, "OWN", "Before", DateTime.UtcNow);
        db.Add(definition);
        await db.SaveChangesAsync();
        definition.Update("After", null, null, null, DateTime.UtcNow);
        await db.SaveChangesAsync();
        Assert.Equal("After", (await db.WorkflowDefinitions.SingleAsync()).Name);
        db.Remove(definition);
        await db.SaveChangesAsync();
        Assert.Empty(await db.WorkflowDefinitions.ToListAsync());
    }
}
