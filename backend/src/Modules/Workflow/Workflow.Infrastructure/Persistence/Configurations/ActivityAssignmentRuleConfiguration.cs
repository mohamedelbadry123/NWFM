namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;

public sealed class ActivityAssignmentRuleConfiguration
    : IEntityTypeConfiguration<ActivityAssignmentRule>
{
    public void Configure(EntityTypeBuilder<ActivityAssignmentRule> builder)
    {
        builder.ToTable("workflow_activity_assignment_rules", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ActivityDefinitionId).IsRequired();
        builder.Property(x => x.AssigneeType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.AssignmentPurpose).HasMaxLength(100);
        builder.Property(x => x.AssignmentKey).HasMaxLength(100);
        builder.Property(x => x.ReferenceId);
        builder.Property(x => x.Expression).HasMaxLength(500);
        builder.Property(x => x.Priority).IsRequired();
        builder.Property(x => x.IsFallback).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);

        builder.HasIndex(x => x.ActivityDefinitionId);
    }
}
