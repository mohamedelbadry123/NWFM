namespace Workflow.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NWFM.Shared.Abstractions;

internal sealed class WorkflowDbContextFactory : IDesignTimeDbContextFactory<WorkflowDbContext>
{
    public WorkflowDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseSqlServer(
                "Server=.;Database=NWFM;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=true",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "Workflow"))
            .Options;

        return new WorkflowDbContext(options, new DesignTimeCurrentTenant());
    }

    private sealed class DesignTimeCurrentTenant : ICurrentTenant
    {
        public Guid OrganizationId => Guid.Empty;
    }
}
