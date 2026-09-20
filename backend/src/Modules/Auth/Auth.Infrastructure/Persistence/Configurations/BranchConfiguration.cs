using Auth.Domain.Entities.Lookups;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auth.Infrastructure.Persistence.Configurations;

public sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("LKP_BRANCH");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Code).HasMaxLength(50).IsRequired();
        builder.Property(e => e.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(e => e.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(e => e.CbuCode).HasMaxLength(50);
        builder.Property(e => e.BranchCode).HasMaxLength(50);
        builder.HasIndex(e => e.Code).IsUnique();
    }
}
