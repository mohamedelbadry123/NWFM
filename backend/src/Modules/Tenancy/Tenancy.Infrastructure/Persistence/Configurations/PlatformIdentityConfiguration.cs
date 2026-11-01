using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tenancy.Domain.Entities;

namespace Tenancy.Infrastructure.Persistence.Configurations;

public sealed class PlatformUserConfiguration : IEntityTypeConfiguration<PlatformUser>
{
    public void Configure(EntityTypeBuilder<PlatformUser> builder)
    {
        builder.ToTable("PlatformUsers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Email).HasMaxLength(PlatformUser.EmailMaxLength).IsRequired();
        builder.Property(x => x.NormalizedEmail).HasMaxLength(PlatformUser.EmailMaxLength).IsRequired();
        builder.HasIndex(x => x.NormalizedEmail).IsUnique();
        builder.Property(x => x.DisplayName).HasMaxLength(PlatformUser.NameMaxLength).IsRequired();
        builder.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Role).HasMaxLength(50).IsRequired();
    }
}

public sealed class SupportAccessGrantConfiguration : IEntityTypeConfiguration<SupportAccessGrant>
{
    public void Configure(EntityTypeBuilder<SupportAccessGrant> builder)
    {
        builder.ToTable("SupportAccessGrants");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Scope).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Reason).HasMaxLength(SupportAccessGrant.ReasonMaxLength).IsRequired();
        builder.Property(x => x.GrantedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.GrantedByUserName).HasMaxLength(SupportAccessGrant.UserNameMaxLength).IsRequired();
        builder.Property(x => x.RevokedByUserId).HasMaxLength(450);
        builder.HasIndex(x => new { x.TenantId, x.ExpiresAtUtc });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SupportSessionConfiguration : IEntityTypeConfiguration<SupportSession>
{
    public void Configure(EntityTypeBuilder<SupportSession> builder)
    {
        builder.ToTable("SupportSessions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Scope).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.PlatformUserName).HasMaxLength(PlatformUser.NameMaxLength).IsRequired();
        builder.Property(x => x.TicketReference).HasMaxLength(SupportSession.TicketMaxLength).IsRequired();
        builder.Property(x => x.ImpersonatedUserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.ImpersonatedUserName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.HandoffCodeHash).HasMaxLength(SupportSession.HandoffCodeHashLength);
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.HandoffCodeHash).IsUnique().HasFilter("[HandoffCodeHash] IS NOT NULL");
        builder.HasIndex(x => x.GrantId);
        builder.HasOne<SupportAccessGrant>().WithMany().HasForeignKey(x => x.GrantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlatformUser>().WithMany().HasForeignKey(x => x.PlatformUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PlatformAuditEntryConfiguration : IEntityTypeConfiguration<PlatformAuditEntry>
{
    public void Configure(EntityTypeBuilder<PlatformAuditEntry> builder)
    {
        builder.ToTable("PlatformAuditEntries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasMaxLength(PlatformAuditEntry.ActionMaxLength).IsRequired();
        builder.Property(x => x.ActorId).HasMaxLength(PlatformAuditEntry.ActorMaxLength);
        builder.Property(x => x.ActorName).HasMaxLength(PlatformAuditEntry.ActorMaxLength);
        builder.Property(x => x.TargetType).HasMaxLength(PlatformAuditEntry.TargetMaxLength);
        builder.Property(x => x.TargetId).HasMaxLength(PlatformAuditEntry.TargetMaxLength);
        builder.Property(x => x.Details).HasMaxLength(PlatformAuditEntry.DetailsMaxLength);
        builder.Property(x => x.IpAddress).HasMaxLength(PlatformAuditEntry.IpMaxLength);
        builder.HasIndex(x => x.AtUtc);
        builder.HasIndex(x => x.TenantId);
    }
}

public sealed class CatalogDataProtectionKeyConfiguration : IEntityTypeConfiguration<CatalogDataProtectionKey>
{
    public void Configure(EntityTypeBuilder<CatalogDataProtectionKey> builder)
    {
        builder.ToTable("DataProtectionKeys");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FriendlyName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Xml).IsRequired();
    }
}
