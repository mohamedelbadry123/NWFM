using Auth.Domain.Entities.Lookups;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auth.Infrastructure.Persistence.Configurations;

public sealed class FieldActivityTypeConfiguration : IEntityTypeConfiguration<FieldActivityType>
{
    public void Configure(EntityTypeBuilder<FieldActivityType> builder)
    {
        builder.ToTable("LKP_FIELD_ACTIVITY_TYPE");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasMany(x => x.Sources).WithOne().HasForeignKey(x => x.FieldActivityTypeId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Sources).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class FieldActivityTypeSourceConfiguration : IEntityTypeConfiguration<FieldActivityTypeSource>
{
    public void Configure(EntityTypeBuilder<FieldActivityTypeSource> builder)
    {
        builder.ToTable("LKP_FIELD_ACTIVITY_TYPE_SOURCE");
        builder.HasKey(x => new { x.FieldActivityTypeId, x.SourceCode });
        builder.Property(x => x.SourceCode).HasMaxLength(50).IsRequired();
        builder.HasOne<ActivitySource>().WithMany().HasForeignKey(x => x.SourceCode)
            .HasPrincipalKey(x => x.Code).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ActivitySourceConfiguration : IEntityTypeConfiguration<ActivitySource>
{
    public void Configure(EntityTypeBuilder<ActivitySource> builder)
    {
        builder.ToTable("LKP_ACTIVITY_SOURCE");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Kind).HasMaxLength(ActivitySourceKinds.MaxLength).IsRequired();
        builder.Property(x => x.Url).HasMaxLength(ActivitySource.UrlMaxLength);
        builder.HasIndex(x => x.Code).IsUnique();
    }
}
