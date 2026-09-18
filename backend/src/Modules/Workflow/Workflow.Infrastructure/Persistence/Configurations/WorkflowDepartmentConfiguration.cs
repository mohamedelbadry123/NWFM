namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;

public sealed class WorkflowDepartmentConfiguration
    : IEntityTypeConfiguration<WorkflowDepartment>
{
    public void Configure(EntityTypeBuilder<WorkflowDepartment> builder)
    {
        builder.ToTable("workflow_departments", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200);
        builder.Property(x => x.Code).HasMaxLength(50);
        builder.Property(x => x.DefaultAssignmentGroupId);
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => x.OrganizationId);
        builder.HasIndex(x => x.IsActive);

        builder.HasMany(x => x.Members)
            .WithOne(x => x.Department)
            .HasForeignKey(x => x.DepartmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
