namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;

public sealed class WorkflowDepartmentMemberConfiguration
    : IEntityTypeConfiguration<WorkflowDepartmentMember>
{
    public void Configure(EntityTypeBuilder<WorkflowDepartmentMember> builder)
    {
        builder.ToTable("workflow_department_members", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DepartmentId).IsRequired();
        builder.Property(x => x.ParticipantId).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);

        builder.HasIndex(x => new { x.DepartmentId, x.ParticipantId }).IsUnique();
        builder.HasIndex(x => x.ParticipantId);

        builder.HasOne(x => x.Participant)
            .WithMany()
            .HasForeignKey(x => x.ParticipantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
