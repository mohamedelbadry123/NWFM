namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

public sealed class WorkflowIntegrationInboxConfiguration : IEntityTypeConfiguration<WorkflowIntegrationInbox>
{
    public void Configure(EntityTypeBuilder<WorkflowIntegrationInbox> builder)
    {
        builder.ToTable("workflow_integration_inbox", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.MessageId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(256);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ModuleKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.BusinessEntityType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.BusinessEntityId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.TriggerEvent).HasMaxLength(200).IsRequired();
        builder.Property(x => x.PayloadJson).IsRequired();
        builder.Property(x => x.Status)
            .HasConversion<string>().HasMaxLength(20).IsRequired()
            .HasDefaultValue(WorkflowInboxStatus.Pending);
        builder.Property(x => x.AttemptCount).IsRequired();
        builder.Property(x => x.ProcessedAt);
        builder.Property(x => x.ErrorMessage).HasMaxLength(4000);
        builder.Property(x => x.BindingId);
        builder.Property(x => x.WorkflowInstanceId);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => x.MessageId).IsUnique();
        builder.HasIndex(x => new { x.Status, x.CreatedAt });
        builder.HasIndex(x => x.OrganizationId);
        builder.HasIndex(x => x.IdempotencyKey);
    }
}
