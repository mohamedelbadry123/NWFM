namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

public sealed class WorkflowNotificationLogConfiguration : IEntityTypeConfiguration<WorkflowNotificationLog>
{
    public void Configure(EntityTypeBuilder<WorkflowNotificationLog> builder)
    {
        builder.ToTable("workflow_notification_logs", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.TemplateKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Channels).HasMaxLength(100).IsRequired();
        builder.Property(x => x.RecipientsJson).IsRequired();
        builder.Property(x => x.VariablesJson).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(256);
        builder.Property(x => x.Status)
            .HasConversion<string>().HasMaxLength(20).IsRequired()
            .HasDefaultValue(WorkflowNotificationLogStatus.Logged).HasSentinel((WorkflowNotificationLogStatus)(-1));
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);

        builder.HasIndex(x => x.OrganizationId);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.TemplateKey);
    }
}
