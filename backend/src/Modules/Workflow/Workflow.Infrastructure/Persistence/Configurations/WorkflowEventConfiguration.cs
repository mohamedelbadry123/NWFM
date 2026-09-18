namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;

/// <summary>Append-only event ledger — no UpdatedAt, no RowVersion, no delete cascade.</summary>
public sealed class WorkflowEventConfiguration : IEntityTypeConfiguration<WorkflowEvent>
{
    public void Configure(EntityTypeBuilder<WorkflowEvent> builder)
    {
        builder.ToTable("workflow_events", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.WorkflowInstanceId).IsRequired();
        builder.Property(x => x.EventType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.ActivityNodeKey).HasMaxLength(200);
        builder.Property(x => x.ActorUserId);
        builder.Property(x => x.PayloadJson).HasMaxLength(4000);
        builder.Property(x => x.OccurredAt).IsRequired();

        builder.HasIndex(x => x.WorkflowInstanceId);
        builder.HasIndex(x => new { x.OrganizationId, x.OccurredAt });
    }
}
