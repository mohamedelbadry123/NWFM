namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;

public sealed class TransitionInstanceConfiguration : IEntityTypeConfiguration<TransitionInstance>
{
    public void Configure(EntityTypeBuilder<TransitionInstance> builder)
    {
        builder.ToTable("transition_instances", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.WorkflowInstanceId).IsRequired();
        builder.Property(x => x.FromActivityNodeKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ToActivityNodeKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TransitionKey).HasMaxLength(200);
        builder.Property(x => x.ConditionExpression).HasMaxLength(2000);
        builder.Property(x => x.WasDefault).IsRequired();
        builder.Property(x => x.TakenAt).IsRequired();
        builder.Property(x => x.ActorUserId);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);

        builder.HasIndex(x => x.WorkflowInstanceId);
        builder.HasIndex(x => x.OrganizationId);
    }
}
