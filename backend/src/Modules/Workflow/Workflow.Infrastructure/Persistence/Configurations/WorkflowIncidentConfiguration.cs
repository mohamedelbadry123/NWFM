namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

public sealed class WorkflowIncidentConfiguration : IEntityTypeConfiguration<WorkflowIncident>
{
    public void Configure(EntityTypeBuilder<WorkflowIncident> builder)
    {
        builder.ToTable("workflow_incidents", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.WorkflowInstanceId).IsRequired();
        builder.Property(x => x.ActivityInstanceId);
        builder.Property(x => x.ActivityNodeKey).HasMaxLength(200);
        builder.Property(x => x.IncidentType)
            .HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.Severity)
            .HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status)
            .HasConversion<string>().HasMaxLength(20).IsRequired()
            .HasDefaultValue(WorkflowIncidentStatus.Open);
        builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
        builder.Property(x => x.ErrorCode).HasMaxLength(100);
        builder.Property(x => x.ErrorMessage).HasMaxLength(2000);
        builder.Property(x => x.ResolvedAt);
        builder.Property(x => x.ResolvedByUserId);
        builder.Property(x => x.ResolutionNotes).HasMaxLength(2000);
        builder.Property(x => x.IgnoredAt);
        builder.Property(x => x.IgnoredByUserId);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.Status, x.Severity });
        builder.HasIndex(x => new { x.WorkflowInstanceId, x.Status });
        builder.HasIndex(x => x.OrganizationId);
    }
}
