namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;

public sealed class ActivityActionDefinitionConfiguration : IEntityTypeConfiguration<ActivityActionDefinition>
{
    public void Configure(EntityTypeBuilder<ActivityActionDefinition> builder)
    {
        builder.ToTable("workflow_activity_action_definitions", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.WorkflowVersionId).IsRequired();
        builder.Property(x => x.ActivityDefinitionId).IsRequired();
        builder.Property(x => x.ActionKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ExecutionTrigger).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.OutcomeKey).HasMaxLength(100);
        builder.Property(x => x.ConditionExpression).HasMaxLength(500);
        builder.Property(x => x.Sequence).IsRequired();
        builder.Property(x => x.InputMappingJson);
        builder.Property(x => x.OutputMappingJson);
        builder.Property(x => x.FailurePolicy).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.RetryCount).IsRequired();
        builder.Property(x => x.RetryDelaySeconds).IsRequired();
        builder.Property(x => x.TimeoutSeconds).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);

        builder.HasIndex(x => new { x.WorkflowVersionId, x.ActivityDefinitionId, x.Sequence });
        builder.HasIndex(x => new { x.WorkflowVersionId, x.ActivityDefinitionId, x.ActionKey });
        builder.HasIndex(x => x.ActivityDefinitionId);

        // Cascade via ActivityDefinition only — cascading both Version and Activity paths
        // triggers SQL Server error 1785 (multiple cascade paths).
        builder.HasOne<ActivityDefinition>()
            .WithMany(a => a.Actions)
            .HasForeignKey(x => x.ActivityDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<WorkflowVersion>()
            .WithMany(v => v.Actions)
            .HasForeignKey(x => x.WorkflowVersionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
