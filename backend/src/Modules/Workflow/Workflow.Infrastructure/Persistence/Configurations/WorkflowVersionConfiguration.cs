namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;

public sealed class WorkflowVersionConfiguration : IEntityTypeConfiguration<WorkflowVersion>
{
    public void Configure(EntityTypeBuilder<WorkflowVersion> builder)
    {
        builder.ToTable("workflow_versions", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.WorkflowDefinitionId).IsRequired();
        builder.Property(x => x.VersionNumber).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.XmlContent).IsRequired();
        builder.Property(x => x.XmlHash).HasMaxLength(64);
        builder.Property(x => x.SchemaVersion).HasMaxLength(10).IsRequired();
        builder.Property(x => x.DesignerJson);
        builder.Property(x => x.ValidationStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.ValidationResultJson);
        builder.Property(x => x.ChangeSummary).HasMaxLength(500);
        builder.Property(x => x.CreatedByUserId).IsRequired();
        builder.Property(x => x.PublishedByUserId);
        builder.Property(x => x.PublishedAt);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.WorkflowDefinitionId, x.VersionNumber }).IsUnique();
        builder.HasIndex(x => x.WorkflowDefinitionId);

        builder.HasMany(x => x.Activities)
            .WithOne()
            .HasForeignKey(x => x.WorkflowVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Transitions)
            .WithOne()
            .HasForeignKey(x => x.WorkflowVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Variables)
            .WithOne()
            .HasForeignKey(x => x.WorkflowVersionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
