using FormEngine.Domain.Constants;
using FormEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FormEngine.Infrastructure.Persistence.Configurations;

public sealed class FieldCatalogEntryConfiguration : IEntityTypeConfiguration<FieldCatalogEntry>
{
    public void Configure(EntityTypeBuilder<FieldCatalogEntry> builder)
    {
        builder.ToTable(FormEngineSchema.FieldCatalog, FormEngineSchema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.DataName).HasMaxLength(FieldCatalogEntry.DataNameMaxLength).IsRequired();
        builder.Property(x => x.FieldType).HasMaxLength(FieldCatalogEntry.FieldTypeMaxLength).IsRequired();
        builder.Property(x => x.LabelEn).HasMaxLength(FieldCatalogEntry.LabelMaxLength);
        builder.Property(x => x.LabelAr).HasMaxLength(FieldCatalogEntry.LabelMaxLength);
        builder.Property(x => x.Description).HasMaxLength(FieldCatalogEntry.DescriptionMaxLength);
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        // The name is what makes a column shared across forms, so it is unique by construction.
        builder.HasIndex(x => x.DataName).IsUnique();
    }
}
