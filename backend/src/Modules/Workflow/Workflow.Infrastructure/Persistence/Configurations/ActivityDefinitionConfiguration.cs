namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;

public sealed class ActivityDefinitionConfiguration : IEntityTypeConfiguration<ActivityDefinition>
{
    public void Configure(EntityTypeBuilder<ActivityDefinition> builder)
    {
        builder.ToTable("workflow_activity_definitions", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.WorkflowVersionId).IsRequired();
        builder.Property(x => x.NodeKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ActivityType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200);
        builder.Property(x => x.ActionKey).HasMaxLength(100);
        builder.Property(x => x.ConfigurationJson);
        builder.Property(x => x.PositionX);
        builder.Property(x => x.PositionY);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);

        builder.HasIndex(x => new { x.WorkflowVersionId, x.NodeKey }).IsUnique();

        builder.HasMany(x => x.AssignmentRules)
            .WithOne()
            .HasForeignKey(x => x.ActivityDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Outcomes)
            .WithOne()
            .HasForeignKey(x => x.ActivityDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Actions)
            .WithOne()
            .HasForeignKey(x => x.ActivityDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
