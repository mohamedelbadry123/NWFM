namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;

public sealed class WorkItemCandidateConfiguration : IEntityTypeConfiguration<WorkItemCandidate>
{
    public void Configure(EntityTypeBuilder<WorkItemCandidate> builder)
    {
        builder.ToTable("work_item_candidates", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.WorkItemId).IsRequired();
        builder.Property(x => x.CandidateType)
            .HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.ReferenceId);
        builder.Property(x => x.ReferenceKey).HasMaxLength(200);
        builder.Property(x => x.DisplayName).HasMaxLength(300);
        builder.Property(x => x.IsPrimary).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);

        builder.HasIndex(x => x.WorkItemId);
        builder.HasIndex(x => x.OrganizationId);
    }
}
