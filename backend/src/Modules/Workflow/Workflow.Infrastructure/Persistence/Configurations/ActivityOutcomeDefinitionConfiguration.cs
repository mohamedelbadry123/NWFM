namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;

public sealed class ActivityOutcomeDefinitionConfiguration : IEntityTypeConfiguration<ActivityOutcomeDefinition>
{
    public void Configure(EntityTypeBuilder<ActivityOutcomeDefinition> builder)
    {
        builder.ToTable("workflow_activity_outcome_definitions", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.WorkflowVersionId).IsRequired();
        builder.Property(x => x.ActivityDefinitionId).IsRequired();
        builder.Property(x => x.OutcomeKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.DescriptionAr).HasMaxLength(1000);
        builder.Property(x => x.SortOrder).IsRequired();
        builder.Property(x => x.RequiresComment).IsRequired();
        builder.Property(x => x.RequiresAttachment).IsRequired();
        builder.Property(x => x.IsDefault).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.ResultValue).HasMaxLength(200);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);

        builder.HasIndex(x => new { x.WorkflowVersionId, x.ActivityDefinitionId, x.OutcomeKey }).IsUnique();
        builder.HasIndex(x => x.ActivityDefinitionId);

        // Cascade via ActivityDefinition only — cascading both Version and Activity paths
        // triggers SQL Server error 1785 (multiple cascade paths).
        builder.HasOne<ActivityDefinition>()
            .WithMany(a => a.Outcomes)
            .HasForeignKey(x => x.ActivityDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<WorkflowVersion>()
            .WithMany(v => v.Outcomes)
            .HasForeignKey(x => x.WorkflowVersionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
