namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;

public sealed class WorkflowGroupMemberConfiguration
    : IEntityTypeConfiguration<WorkflowGroupMember>
{
    public void Configure(EntityTypeBuilder<WorkflowGroupMember> builder)
    {
        builder.ToTable("workflow_group_members", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AssignmentGroupId).IsRequired();
        builder.Property(x => x.ParticipantId).IsRequired();
        builder.Property(x => x.CanClaim).IsRequired();
        builder.Property(x => x.IsPrimary).IsRequired();
        builder.Property(x => x.ValidFrom);
        builder.Property(x => x.ValidTo);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);

        builder.HasIndex(x => new { x.AssignmentGroupId, x.ParticipantId }).IsUnique();
        builder.HasIndex(x => x.ParticipantId);

        builder.HasOne(x => x.Participant)
            .WithMany()
            .HasForeignKey(x => x.ParticipantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
