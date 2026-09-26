using Auth.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auth.Infrastructure.Persistence.Configurations;

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permissions");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Code).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Module).HasMaxLength(100).IsRequired();
        builder.Property(e => e.NameEn).HasMaxLength(300).IsRequired();
        builder.Property(e => e.NameAr).HasMaxLength(300).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.HasIndex(e => e.Code).IsUnique();
    }
}
