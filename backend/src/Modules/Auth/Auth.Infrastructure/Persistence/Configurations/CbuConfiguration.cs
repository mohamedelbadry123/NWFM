using Auth.Domain.Entities.Lookups;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auth.Infrastructure.Persistence.Configurations;

public sealed class CbuConfiguration : IEntityTypeConfiguration<Cbu>
{
    public void Configure(EntityTypeBuilder<Cbu> builder)
    {
        builder.ToTable("LKP_CBU");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Code).HasMaxLength(50).IsRequired();
        builder.Property(e => e.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(e => e.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(e => e.ClusterCode).HasMaxLength(50).IsRequired();
        builder.HasIndex(e => e.Code).IsUnique();
    }
}
