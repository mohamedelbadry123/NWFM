namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;

public sealed class WorkflowConnectionConfiguration : IEntityTypeConfiguration<WorkflowIntegrationConnection>
{
    public void Configure(EntityTypeBuilder<WorkflowIntegrationConnection> b)
    {
        b.ToTable("integration_connections", "Workflow"); b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200); b.Property(x => x.Kind).HasMaxLength(20);
        b.Property(x => x.Address).HasMaxLength(2000); b.Property(x => x.Authentication).HasMaxLength(40);
        b.Property(x => x.RowVersion).IsRowVersion(); b.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique();
    }
}
public sealed class WorkflowJobConfiguration : IEntityTypeConfiguration<WorkflowIntegrationJob>
{
    public void Configure(EntityTypeBuilder<WorkflowIntegrationJob> b)
    {
        b.ToTable("integration_jobs", "Workflow"); b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasMaxLength(20); b.Property(x => x.Status).HasMaxLength(20);
        b.Property(x => x.OperationKey).HasMaxLength(450); b.Property(x => x.Error).HasMaxLength(2000);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.OrganizationId, x.OperationKey }).IsUnique();
        b.HasIndex(x => new { x.Status, x.NextAttemptAt }); b.HasIndex(x => x.ActivityInstanceId);
    }
}
public sealed class WorkflowSubscriptionConfiguration : IEntityTypeConfiguration<WorkflowEventSubscription>
{
    public void Configure(EntityTypeBuilder<WorkflowEventSubscription> b)
    {
        b.ToTable("event_subscriptions", "Workflow"); b.HasKey(x => x.Id);
        b.Property(x => x.EventKey).HasMaxLength(200); b.Property(x => x.CorrelationId).HasMaxLength(256);
        b.Property(x => x.Status).HasMaxLength(20); b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => x.ActivityInstanceId).IsUnique();
        b.HasIndex(x => new { x.OrganizationId, x.ConnectionId, x.EventKey, x.CorrelationId });
    }
}
public sealed class WorkflowReceiptConfiguration : IEntityTypeConfiguration<WorkflowEventReceipt>
{
    public void Configure(EntityTypeBuilder<WorkflowEventReceipt> b)
    {
        b.ToTable("event_receipts", "Workflow"); b.HasKey(x => x.Id);
        b.Property(x => x.EventId).HasMaxLength(200); b.Property(x => x.EventKey).HasMaxLength(200);
        b.Property(x => x.CorrelationId).HasMaxLength(256); b.Property(x => x.Status).HasMaxLength(20);
        b.Property(x => x.Error).HasMaxLength(2000); b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.OrganizationId, x.ConnectionId, x.EventId }).IsUnique();
        b.HasIndex(x => new { x.OrganizationId, x.ConnectionId, x.EventKey, x.CorrelationId });
    }
}
