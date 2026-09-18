namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

public sealed class WorkItemConfiguration : IEntityTypeConfiguration<WorkItem>
{
    public void Configure(EntityTypeBuilder<WorkItem> builder)
    {
        builder.ToTable("work_items", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.WorkflowInstanceId).IsRequired();
        builder.Property(x => x.ActivityInstanceId).IsRequired();
        builder.Property(x => x.AssignmentGroupId).IsRequired();
        builder.Property(x => x.ClaimedByUserId);
        builder.Property(x => x.ClaimedAt);
        builder.Property(x => x.CompletedByUserId);
        builder.Property(x => x.CompletedAt);
        builder.Property(x => x.DueAt);
        builder.Property(x => x.Status)
            .HasConversion<string>().HasMaxLength(20).IsRequired()
            .HasDefaultValue(WorkItemStatus.Pending);
        builder.Property(x => x.ActionTaken).HasMaxLength(200);
        builder.Property(x => x.CommentText).HasMaxLength(4000);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => x.WorkflowInstanceId);
        builder.HasIndex(x => new { x.OrganizationId, x.AssignmentGroupId, x.Status });
        builder.HasIndex(x => new { x.OrganizationId, x.ClaimedByUserId, x.Status });
    }
}
