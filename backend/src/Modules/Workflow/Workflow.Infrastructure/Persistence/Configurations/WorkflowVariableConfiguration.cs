namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;

public sealed class WorkflowVariableConfiguration : IEntityTypeConfiguration<WorkflowVariable>
{
    public void Configure(EntityTypeBuilder<WorkflowVariable> builder)
    {
        builder.ToTable("workflow_variables", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.WorkflowInstanceId).IsRequired();
        builder.Property(x => x.VariableName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ValueJson).HasMaxLength(4000);
        builder.Property(x => x.DataType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);

        builder.HasIndex(x => new { x.WorkflowInstanceId, x.VariableName }).IsUnique();
        builder.HasIndex(x => x.OrganizationId);
    }
}
