namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;

public sealed class WorkflowTransitionConfiguration : IEntityTypeConfiguration<WorkflowTransition>
{
    public void Configure(EntityTypeBuilder<WorkflowTransition> builder)
    {
        builder.ToTable("workflow_transitions", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.WorkflowVersionId).IsRequired();
        builder.Property(x => x.FromActivityDefinitionId).IsRequired();
        builder.Property(x => x.ToActivityDefinitionId).IsRequired();
        builder.Property(x => x.TransitionKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ConditionExpression).HasMaxLength(500);
        builder.Property(x => x.IsDefault).IsRequired();
        builder.Property(x => x.Priority).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);

        builder.HasIndex(x => new { x.WorkflowVersionId, x.TransitionKey }).IsUnique();
        builder.HasIndex(x => x.FromActivityDefinitionId);
        builder.HasIndex(x => x.ToActivityDefinitionId);
    }
}
