using FormEngine.Domain.Constants;
using FormEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FormEngine.Infrastructure.Persistence.Configurations;

public sealed class FormVersionConfiguration : IEntityTypeConfiguration<FormVersion>
{
    public void Configure(EntityTypeBuilder<FormVersion> builder)
    {
        builder.ToTable(FormEngineSchema.FormVersions, FormEngineSchema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.FormDefinitionId).IsRequired();
        builder.Property(x => x.VersionNo).IsRequired();
        builder.Property(x => x.TargetClient).HasMaxLength(FormVersion.TargetClientMaxLength).IsRequired();
        builder.Property(x => x.SchemaJson).IsRequired();
        builder.Property(x => x.SnapshotJson).IsRequired();
        builder.Property(x => x.PublishedBy).HasMaxLength(FormDefinition.ActorMaxLength);
        builder.Property(x => x.PublishedAt).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => new { x.FormDefinitionId, x.VersionNo, x.TargetClient }).IsUnique();
    }
}
