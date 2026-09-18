namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

public sealed class ActivityInstanceConfiguration : IEntityTypeConfiguration<ActivityInstance>
{
    public void Configure(EntityTypeBuilder<ActivityInstance> builder)
    {
        builder.ToTable("activity_instances", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.WorkflowInstanceId).IsRequired();
        builder.Property(x => x.ActivityNodeKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ActivityType)
            .HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Status)
            .HasConversion<string>().HasMaxLength(20).IsRequired()
            .HasDefaultValue(ActivityInstanceStatus.Pending);
        builder.Property(x => x.StartedAt).IsRequired();
        builder.Property(x => x.CompletedAt);
        builder.Property(x => x.FailedAt);
        builder.Property(x => x.FailureReason).HasMaxLength(2000);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);

        builder.HasIndex(x => x.WorkflowInstanceId);
        builder.HasIndex(x => x.OrganizationId);
    }
}
