using Auth.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auth.Infrastructure.Persistence.Configurations;

public sealed class OrgScopeConfiguration : IEntityTypeConfiguration<OrgScope>
{
    public void Configure(EntityTypeBuilder<OrgScope> builder)
    {
        builder.ToTable("OrgScopes");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.OwnerType).HasMaxLength(50).IsRequired();
        builder.Property(e => e.OwnerId).HasMaxLength(450).IsRequired();
        builder.Property(e => e.Level).HasMaxLength(50);
        builder.Property(e => e.Code).HasMaxLength(100);
        builder.Ignore(e => e.HasTerritory);

        builder.HasIndex(e => new { e.OwnerType, e.OwnerId });
    }
}
