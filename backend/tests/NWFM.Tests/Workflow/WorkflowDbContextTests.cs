namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Persistence;
using global::Workflow.Infrastructure.Persistence;

public sealed class WorkflowDbContextTests
{
    [Fact]
    public void WorkflowDbContext_InheritsFrom_BaseDbContext()
        => typeof(WorkflowDbContext).BaseType.Should().Be(typeof(BaseDbContext));

    [Fact]
    public void WorkflowDbContext_Constructor_AcceptsICurrentTenant()
    {
        var options = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseSqlServer("Server=.;Database=test;Trusted_Connection=True")
            .Options;

        var act = () => new WorkflowDbContext(options, new StubTenant());

        act.Should().NotThrow();
    }

    private sealed class StubTenant : ICurrentTenant
    {
        public Guid OrganizationId => Guid.NewGuid();
    }
}
