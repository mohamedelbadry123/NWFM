using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;

namespace Workflow.Infrastructure.Persistence.Configurations;

public sealed class WorkflowMappingInvocationConfiguration : IEntityTypeConfiguration<WorkflowMappingInvocation>
{
    public void Configure(EntityTypeBuilder<WorkflowMappingInvocation> b)
    {
        b.ToTable("mapping_invocations", "Workflow"); b.HasKey(x => x.Id);
        b.Property(x => x.TargetNodeKey).HasMaxLength(200);
        b.Property(x => x.ActionId).HasMaxLength(200);
        b.Property(x => x.OperationKey).HasMaxLength(450);
        b.Property(x => x.ExecutionKind).HasMaxLength(20);
        b.Property(x => x.Status).HasMaxLength(20);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Property(x => x.WaitDependenciesJson).HasDefaultValue("[]");
        b.HasIndex(x => new { x.OrganizationId, x.OperationKey }).IsUnique();
        b.HasIndex(x => new { x.ParentInvocationId, x.ActionId }).IsUnique().HasFilter("[ParentInvocationId] IS NOT NULL");
        b.HasIndex(x => new { x.Status, x.Deadline });
        b.HasIndex(x => new { x.RootInstanceId, x.TargetInstanceId });
        b.HasIndex(x => new { x.OriginActivityId, x.FormVisitId });
    }
}
