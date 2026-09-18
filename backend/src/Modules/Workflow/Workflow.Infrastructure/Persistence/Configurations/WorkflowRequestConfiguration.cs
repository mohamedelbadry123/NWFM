namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

public sealed class WorkflowRequestConfiguration : IEntityTypeConfiguration<WorkflowRequest>
{
    public void Configure(EntityTypeBuilder<WorkflowRequest> builder)
    {
        builder.ToTable("workflow_requests", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.RequestNumber).HasMaxLength(40).IsRequired();
        builder.Property(x => x.WorkflowBindingId).IsRequired();
        builder.Property(x => x.WorkflowInstanceId).IsRequired();
        builder.Property(x => x.BusinessEntityType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.BusinessEntityId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ServiceKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ServiceNameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ServiceNameAr).HasMaxLength(200);
        builder.Property(x => x.ScreenKey).HasMaxLength(200);
        builder.Property(x => x.TriggerEventKey).HasMaxLength(150).IsRequired();
        builder.Property(x => x.RequestDate).IsRequired();
        builder.Property(x => x.RequesterUserId);
        builder.Property(x => x.Status)
            .HasConversion<string>().HasMaxLength(20).IsRequired()
            .HasDefaultValue(WorkflowInstanceStatus.Running).HasSentinel((WorkflowInstanceStatus)(-1));
        builder.Property(x => x.CurrentActivityInstanceId);
        builder.Property(x => x.CurrentActivityNameEn).HasMaxLength(200);
        builder.Property(x => x.CurrentActivityNameAr).HasMaxLength(200);
        builder.Property(x => x.OriginalAssignedGroupId);
        builder.Property(x => x.CurrentAssignedGroupId);
        builder.Property(x => x.CurrentTaskSlaMinutes);
        builder.Property(x => x.CurrentTaskDueAtUtc);
        builder.Property(x => x.CompletedAtUtc);
        builder.Property(x => x.CorrelationId).HasMaxLength(256);
        builder.Property(x => x.CurrentClaimedByUserId);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.OrganizationId, x.RequestNumber }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.WorkflowInstanceId }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.Status });
        builder.HasIndex(x => new { x.OrganizationId, x.CurrentAssignedGroupId, x.Status });
        builder.HasIndex(x => x.CurrentTaskDueAtUtc);
    }
}
