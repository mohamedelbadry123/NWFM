using FormEngine.Domain.Constants;
using FormEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FormEngine.Infrastructure.Persistence.Configurations;

public sealed class FormFieldConfiguration : IEntityTypeConfiguration<FormField>
{
    public void Configure(EntityTypeBuilder<FormField> builder)
    {
        builder.ToTable(FormEngineSchema.FormFields, FormEngineSchema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.DataName).HasMaxLength(FormField.DataNameMaxLength).IsRequired();
        builder.Property(x => x.FieldType).HasMaxLength(FormField.FieldTypeMaxLength).IsRequired();
        builder.Property(x => x.LabelEn).HasMaxLength(FormField.LabelMaxLength);
        builder.Property(x => x.LabelAr).HasMaxLength(FormField.LabelMaxLength);
        builder.Property(x => x.IsCompanion).IsRequired();
        builder.Property(x => x.FirstVersionNo).IsRequired();
        builder.Property(x => x.LastVersionNo).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        // One column per name within a form's table, so one registry row per name within a form.
        builder.HasIndex(x => new { x.FormDefinitionId, x.DataName }).IsUnique();

        // The catalog groups every form's fields by name.
        builder.HasIndex(x => x.DataName);

        builder.HasOne<FormDefinition>()
            .WithMany()
            .HasForeignKey(x => x.FormDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
