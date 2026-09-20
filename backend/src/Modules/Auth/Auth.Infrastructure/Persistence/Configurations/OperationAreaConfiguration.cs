using Auth.Domain.Entities.Lookups;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auth.Infrastructure.Persistence.Configurations;

public sealed class OperationAreaConfiguration : IEntityTypeConfiguration<OperationArea>
{
    public void Configure(EntityTypeBuilder<OperationArea> builder)
    {
        builder.ToTable("LKP_OPERATION_AREA");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Code).HasMaxLength(50).IsRequired();
        builder.Property(e => e.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(e => e.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(e => e.CbuCode).HasMaxLength(50).IsRequired();
        builder.Property(e => e.MainAreaCode).HasMaxLength(50);
        builder.HasIndex(e => e.Code).IsUnique();
    }
}
