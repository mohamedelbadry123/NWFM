namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;

public sealed class WorkflowParticipantConfiguration
    : IEntityTypeConfiguration<WorkflowParticipant>
{
    public void Configure(EntityTypeBuilder<WorkflowParticipant> builder)
    {
        builder.ToTable("workflow_participants", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DisplayNameAr).HasMaxLength(200);
        builder.Property(x => x.Email).HasMaxLength(256).IsRequired();
        builder.Property(x => x.EmployeeNumber).HasMaxLength(50);
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.OrganizationId, x.UserId }).IsUnique();
        builder.HasIndex(x => x.OrganizationId);
        builder.HasIndex(x => x.IsActive);
    }
}
