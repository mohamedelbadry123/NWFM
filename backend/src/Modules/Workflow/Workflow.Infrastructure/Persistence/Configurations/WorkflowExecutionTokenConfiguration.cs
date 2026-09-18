namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

public sealed class WorkflowExecutionTokenConfiguration : IEntityTypeConfiguration<WorkflowExecutionToken>
{
    public void Configure(EntityTypeBuilder<WorkflowExecutionToken> builder)
    {
        builder.ToTable("workflow_execution_tokens", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.WorkflowInstanceId).IsRequired();
        builder.Property(x => x.ParallelGatewayNodeKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.BranchKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status)
            .HasConversion<string>().HasMaxLength(20).IsRequired()
            .HasDefaultValue(ExecutionTokenStatus.Active);
        builder.Property(x => x.JoinNodeKey).HasMaxLength(200);
        builder.Property(x => x.ParentTokenId);
        builder.Property(x => x.CompletedAt);
        builder.Property(x => x.CancelledAt);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.WorkflowInstanceId, x.BranchKey }).IsUnique();
        builder.HasIndex(x => x.OrganizationId);
        builder.HasIndex(x => new { x.WorkflowInstanceId, x.Status });
    }
}
