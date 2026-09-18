namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

public sealed class WorkflowInstanceConfiguration : IEntityTypeConfiguration<WorkflowInstance>
{
    public void Configure(EntityTypeBuilder<WorkflowInstance> builder)
    {
        builder.ToTable("workflow_instances", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.WorkflowBindingId).IsRequired();
        builder.Property(x => x.PinnedWorkflowVersionId).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(256).IsRequired();
        builder.Property(x => x.BusinessEntityId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(256);
        builder.Property(x => x.Status)
            .HasConversion<string>().HasMaxLength(20).IsRequired()
            .HasDefaultValue(WorkflowInstanceStatus.Pending);
        builder.Property(x => x.StartedAt).IsRequired();
        builder.Property(x => x.CompletedAt);
        builder.Property(x => x.CancelledAt);
        builder.Property(x => x.SuspendedAt);
        builder.Property(x => x.FailureReason).HasMaxLength(2000);
        builder.Property(x => x.StartedByUserId);
        builder.Property(x => x.CurrentActivityNodeKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ParentInstanceId);
        builder.Property(x => x.ParentActivityNodeKey).HasMaxLength(200);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.OrganizationId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => x.OrganizationId);
        builder.HasIndex(x => x.WorkflowBindingId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.ParentInstanceId);
    }
}
