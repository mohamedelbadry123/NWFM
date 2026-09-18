namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;

public sealed class WorkflowVariableDefinitionConfiguration
    : IEntityTypeConfiguration<WorkflowVariableDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowVariableDefinition> builder)
    {
        builder.ToTable("workflow_variable_definitions", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.WorkflowVersionId).IsRequired();
        builder.Property(x => x.VariableKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200);
        builder.Property(x => x.DataType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.IsRequired).IsRequired();
        builder.Property(x => x.IsSensitive).IsRequired();
        builder.Property(x => x.DefaultValue).HasMaxLength(2000);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.DescriptionAr).HasMaxLength(1000);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);

        builder.HasIndex(x => new { x.WorkflowVersionId, x.VariableKey }).IsUnique();
    }
}
