namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

public sealed class WorkflowIntegrationOutboxConfiguration : IEntityTypeConfiguration<WorkflowIntegrationOutbox>
{
    public void Configure(EntityTypeBuilder<WorkflowIntegrationOutbox> builder)
    {
        builder.ToTable("workflow_integration_outbox", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.MessageId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(256);
        builder.Property(x => x.BindingId).IsRequired();
        builder.Property(x => x.WorkflowInstanceId).IsRequired();
        builder.Property(x => x.ModuleKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.BusinessEntityType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.BusinessEntityId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.OutcomeKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PayloadJson).HasMaxLength(8000).IsRequired();
        builder.Property(x => x.Status)
            .HasConversion<string>().HasMaxLength(20).IsRequired()
            .HasDefaultValue(WorkflowOutboxStatus.Pending);
        builder.Property(x => x.AttemptCount).IsRequired();
        builder.Property(x => x.PublishedAt);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => x.MessageId).IsUnique();
        builder.HasIndex(x => x.CorrelationId);
        builder.HasIndex(x => new { x.Status, x.CreatedAt });
        builder.HasIndex(x => x.OrganizationId);
    }
}
