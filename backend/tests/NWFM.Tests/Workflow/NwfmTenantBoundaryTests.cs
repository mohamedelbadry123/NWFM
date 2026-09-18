namespace NWFM.Tests.Modules.Workflow;

using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Abstractions;
using global::Workflow.Domain.Entities;
using global::Workflow.Infrastructure.Persistence;

public sealed class NwfmTenantBoundaryTests
{
    private sealed record Tenant(Guid OrganizationId) : ICurrentTenant;

    [Fact]
    public async Task ForeignTenantWriteIsRejected()
    {
        var options = new DbContextOptionsBuilder<WorkflowDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var db = new WorkflowDbContext(options, new Tenant(Guid.NewGuid()));
        db.WorkflowDefinitions.Add(WorkflowDefinition.Create(Guid.NewGuid(), "OTHER", "Other tenant", DateTime.UtcNow));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task VersionAndCompiledActivitiesAreScopedThroughTheirDefinition()
    {
        var options = new DbContextOptionsBuilder<WorkflowDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        Guid versionB;
        using (var db = new WorkflowDbContext(options, new Tenant(b))) {
            var definition = WorkflowDefinition.Create(b, "APPROVAL", "Approval", DateTime.UtcNow);
            var version = WorkflowVersion.CreateDraft(definition.Id, 1, Guid.NewGuid(), DateTime.UtcNow);
            versionB = version.Id;
            db.Add(definition); db.Add(version);
            db.Add(ActivityDefinition.Create(version.Id, "start", global::Workflow.Domain.Enums.ActivityType.Start, "Start", DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
        using var dbA = new WorkflowDbContext(options, new Tenant(a));
        Assert.Null(await dbA.WorkflowVersions.SingleOrDefaultAsync(v => v.Id == versionB));
        Assert.Empty(await dbA.ActivityDefinitions.ToListAsync());
        using var dbB = new WorkflowDbContext(options, new Tenant(b));
        Assert.Single(await dbB.WorkflowVersions.ToListAsync());
        Assert.Single(await dbB.ActivityDefinitions.ToListAsync());
    }
}
