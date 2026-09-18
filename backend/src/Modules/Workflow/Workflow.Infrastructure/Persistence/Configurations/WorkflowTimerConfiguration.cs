namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

public sealed class WorkflowTimerConfiguration : IEntityTypeConfiguration<WorkflowTimer>
{
    public void Configure(EntityTypeBuilder<WorkflowTimer> builder)
    {
        builder.ToTable("workflow_timers", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.WorkflowInstanceId).IsRequired();
        builder.Property(x => x.ActivityInstanceId).IsRequired();
        builder.Property(x => x.TimerType)
            .HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.DueAt).IsRequired();
        builder.Property(x => x.SignalKey).HasMaxLength(200);
        builder.Property(x => x.Status)
            .HasConversion<string>().HasMaxLength(20).IsRequired()
            .HasDefaultValue(WorkflowTimerStatus.Pending);
        builder.Property(x => x.AttemptCount).IsRequired();
        builder.Property(x => x.LastAttemptAt);
        builder.Property(x => x.CompletedAt);
        builder.Property(x => x.CancelledAt);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.Status, x.DueAt });
        builder.HasIndex(x => new { x.OrganizationId, x.Status, x.DueAt });
        builder.HasIndex(x => x.WorkflowInstanceId);
    }
}
