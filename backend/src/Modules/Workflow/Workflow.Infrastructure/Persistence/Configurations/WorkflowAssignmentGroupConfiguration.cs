namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;

public sealed class WorkflowAssignmentGroupConfiguration
    : IEntityTypeConfiguration<WorkflowAssignmentGroup>
{
    public void Configure(EntityTypeBuilder<WorkflowAssignmentGroup> builder)
    {
        builder.ToTable("workflow_assignment_groups", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200);
        builder.Property(x => x.AssignmentStrategy)
            .HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.OrganizationId, x.Code }).IsUnique();
        builder.HasIndex(x => x.OrganizationId);

        builder.HasMany(x => x.Members)
            .WithOne(x => x.AssignmentGroup)
            .HasForeignKey(x => x.AssignmentGroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
