using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;
namespace Workflow.Infrastructure.Persistence.Configurations;

public sealed class WorkflowWorkspaceActionConfiguration : IEntityTypeConfiguration<WorkflowWorkspaceAction>
{
    public void Configure(EntityTypeBuilder<WorkflowWorkspaceAction> b)
    {
        b.ToTable("workspace_actions", "Workflow"); b.HasKey(x => x.Id);
        b.Property(x => x.Fingerprint).HasMaxLength(64);
        b.HasIndex(x => new { x.OrganizationId, x.RequestId }).IsUnique();
    }
}
