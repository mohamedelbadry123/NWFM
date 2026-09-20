using FormEngine.Domain.Constants;
using FormEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FormEngine.Infrastructure.Persistence.Configurations;

public sealed class FormDefinitionConfiguration : IEntityTypeConfiguration<FormDefinition>
{
    public void Configure(EntityTypeBuilder<FormDefinition> builder)
    {
        builder.ToTable(FormEngineSchema.FormDefinitions, FormEngineSchema.Name);
        builder.HasKey(x => x.Id);

        // The entity assigns its own id, so a version reached through the navigation is tracked as
        // new rather than as an existing row EF thinks it has already seen.
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Code).HasMaxLength(FormDefinition.CodeMaxLength).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(FormDefinition.NameMaxLength).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(FormDefinition.NameMaxLength).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.DepartmentCode).HasMaxLength(FormDefinition.DepartmentCodeMaxLength);
        builder.Property(x => x.SchemaJson).IsRequired().HasDefaultValue("{}");
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(FormDefinition.ActorMaxLength);
        builder.Property(x => x.UpdatedBy).HasMaxLength(FormDefinition.ActorMaxLength);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.Category);
        builder.HasIndex(x => x.DepartmentCode);
        builder.HasIndex(x => new { x.Status, x.Category });

        builder.HasMany(x => x.Versions)
            .WithOne()
            .HasForeignKey(x => x.FormDefinitionId)
            // Versions are what pinned consumers resolve against, so a form with history cannot be
            // deleted out from under them by a cascade.
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(x => x.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
