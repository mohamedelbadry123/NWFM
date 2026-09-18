namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;

public sealed class WorkflowBindingAssignmentMappingConfiguration
    : IEntityTypeConfiguration<WorkflowBindingAssignmentMapping>
{
    public void Configure(EntityTypeBuilder<WorkflowBindingAssignmentMapping> builder)
    {
        builder.ToTable("workflow_binding_assignment_mappings", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.WorkflowBindingId).IsRequired();
        builder.Property(x => x.AssignmentKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.AssignmentGroupId).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.RowVersion).IsRowVersion();

        // One mapping per (Org, Binding, AssignmentKey) — org can only map each key once per binding
        builder.HasIndex(x => new { x.OrganizationId, x.WorkflowBindingId, x.AssignmentKey })
            .IsUnique();
        builder.HasIndex(x => x.WorkflowBindingId);
        builder.HasIndex(x => x.OrganizationId);
    }
}
