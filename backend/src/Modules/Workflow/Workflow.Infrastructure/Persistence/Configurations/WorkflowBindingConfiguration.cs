namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

public sealed class WorkflowBindingConfiguration : IEntityTypeConfiguration<WorkflowBinding>
{
    public void Configure(EntityTypeBuilder<WorkflowBinding> builder)
    {
        builder.ToTable("workflow_bindings", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.WorkflowDefinitionId).IsRequired();
        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.ModuleKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TriggerEvent).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20).IsRequired()
            .HasDefaultValue(WorkflowBindingMode.Disabled);
        builder.Property(x => x.VersionPolicy).HasConversion<string>().HasMaxLength(20).IsRequired()
            .HasDefaultValue(WorkflowVersionPolicy.Latest);
        builder.Property(x => x.ExecutionPolicy).HasConversion<string>().HasMaxLength(40).IsRequired()
            .HasDefaultValue(WorkflowExecutionPolicy.StartNewInstance);
        builder.Property(x => x.FixedWorkflowVersionId);
        builder.Property(x => x.StartEventKey).HasMaxLength(200);
        builder.Property(x => x.StartConditionExpression).HasMaxLength(2000);
        builder.Property(x => x.ScreenKey).HasMaxLength(200);
        builder.Property(x => x.InputMappingJson);
        builder.Property(x => x.OutcomeMappingJson);
        builder.Property(x => x.ConditionJson);
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.WorkflowDefinitionId, x.OrganizationId, x.ModuleKey, x.EntityType, x.TriggerEvent })
            .IsUnique();
        builder.HasIndex(x => x.OrganizationId);
        builder.HasIndex(x => x.WorkflowDefinitionId);
    }
}
