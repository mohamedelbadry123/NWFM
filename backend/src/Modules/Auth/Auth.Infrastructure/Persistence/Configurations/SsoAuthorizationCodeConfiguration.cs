using Auth.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auth.Infrastructure.Persistence.Configurations;

public sealed class SsoAuthorizationCodeConfiguration : IEntityTypeConfiguration<SsoAuthorizationCode>
{
    public void Configure(EntityTypeBuilder<SsoAuthorizationCode> builder)
    {
        builder.ToTable("SsoAuthorizationCodes");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).UseIdentityColumn();
        builder.Property(e => e.UserId).HasMaxLength(450).IsRequired();
        builder.Property(e => e.CodeHash).HasMaxLength(128).IsRequired();
        builder.Property(e => e.SessionIndex).HasMaxLength(500);

        builder.HasIndex(e => e.CodeHash);
        builder.HasIndex(e => e.UserId);
    }
}
