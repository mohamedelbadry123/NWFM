using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;

namespace Workflow.Infrastructure.Persistence.Configurations;

public sealed class WorkflowRoutingScopeConfiguration : IEntityTypeConfiguration<WorkflowRoutingScope>
{
    public void Configure(EntityTypeBuilder<WorkflowRoutingScope> b)
    {
        b.ToTable("workflow_routing_scopes", "Workflow"); b.HasKey(x => x.Id);
        b.Property(x => x.ValuesJson).IsRequired(); b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.OrganizationId, x.WorkflowInstanceId });
    }
}
public sealed class WorkflowRoutingResultConfiguration : IEntityTypeConfiguration<WorkflowRoutingResult>
{
    public void Configure(EntityTypeBuilder<WorkflowRoutingResult> b)
    {
        b.ToTable("workflow_routing_results", "Workflow"); b.HasKey(x => x.Id);
        b.Property(x => x.NodeKey).HasMaxLength(200).IsRequired();
        b.Property(x => x.Outcome).HasMaxLength(100).IsRequired(); b.Property(x => x.ValuesJson).IsRequired();
        b.HasIndex(x => x.ActivityInstanceId).IsUnique();
        b.HasIndex(x => new { x.OrganizationId, x.WorkflowInstanceId, x.ExecutionTokenId });
    }
}
public sealed class WorkflowRoutingActivationConfiguration : IEntityTypeConfiguration<WorkflowRoutingActivation>
{
    public void Configure(EntityTypeBuilder<WorkflowRoutingActivation> b)
    {
        b.ToTable("workflow_routing_activations", "Workflow"); b.HasKey(x => x.Id);
        b.Property(x => x.ForkNodeKey).HasMaxLength(200).IsRequired(); b.Property(x => x.JoinNodeKey).HasMaxLength(200).IsRequired();
        b.Property(x => x.Status).HasMaxLength(30).IsRequired(); b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.OrganizationId, x.WorkflowInstanceId }); b.HasIndex(x => new { x.Status, x.DeadlineAt });
    }
}
public sealed class WorkflowRoutingUpgradeSnapshotConfiguration : IEntityTypeConfiguration<WorkflowRoutingUpgradeSnapshot>
{
    public void Configure(EntityTypeBuilder<WorkflowRoutingUpgradeSnapshot> b)
    {
        b.ToTable("workflow_routing_upgrade_snapshots", "Workflow"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.OrganizationId, x.WorkflowVersionId }); b.Property(x => x.Xml).IsRequired();
    }
}
