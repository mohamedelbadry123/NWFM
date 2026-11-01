using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tenancy.Domain.Entities;

namespace Tenancy.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Key).HasMaxLength(TenantKey.MaxLength).IsRequired();
        builder.HasIndex(x => x.Key).IsUnique();

        builder.Property(x => x.Name).HasMaxLength(Tenant.NameMaxLength).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.StatusReason).HasMaxLength(Tenant.StatusReasonMaxLength);
        builder.Property(x => x.ContactName).HasMaxLength(Tenant.ContactMaxLength).IsRequired();
        builder.Property(x => x.ContactEmail).HasMaxLength(Tenant.ContactMaxLength).IsRequired();
        builder.Property(x => x.ContactPhone).HasMaxLength(50);
        builder.Property(x => x.SeedProfile).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.StoragePath).HasMaxLength(Tenant.StoragePathMaxLength);

        builder.OwnsOne(x => x.Settings, settings =>
        {
            settings.Property(s => s.SsoEnabled).HasColumnName("SsoEnabled");
            settings.Property(s => s.SsoEntityId).HasColumnName("SsoEntityId").HasMaxLength(TenantSettings.SsoUrlMaxLength);
            settings.Property(s => s.SsoIdentityProviderEntityId).HasColumnName("SsoIdentityProviderEntityId").HasMaxLength(TenantSettings.SsoUrlMaxLength);
            settings.Property(s => s.SsoMetadataUrl).HasColumnName("SsoMetadataUrl").HasMaxLength(TenantSettings.SsoUrlMaxLength);
            settings.Property(s => s.ListedPublicly).HasColumnName("ListedPublicly");
            settings.Property(s => s.DefaultLanguage).HasColumnName("DefaultLanguage").HasMaxLength(8);
        });
        builder.Navigation(x => x.Settings).IsRequired();

        builder.HasMany(x => x.Hosts).WithOne().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Hosts).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne(x => x.Database).WithOne().HasForeignKey<TenantDatabase>(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Subscription).WithOne().HasForeignKey<Subscription>(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TenantHostConfiguration : IEntityTypeConfiguration<TenantHost>
{
    public void Configure(EntityTypeBuilder<TenantHost> builder)
    {
        builder.ToTable("TenantHosts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Host).HasMaxLength(TenantHost.HostMaxLength).IsRequired();
        builder.HasIndex(x => x.Host).IsUnique();
    }
}

public sealed class TenantDatabaseConfiguration : IEntityTypeConfiguration<TenantDatabase>
{
    public void Configure(EntityTypeBuilder<TenantDatabase> builder)
    {
        builder.ToTable("TenantDatabases");
        builder.HasKey(x => x.TenantId);

        builder.Property(x => x.Mode).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.DatabaseName).HasMaxLength(TenantDatabase.NameMaxLength);
        builder.Property(x => x.LoginName).HasMaxLength(TenantDatabase.NameMaxLength);
        builder.Property(x => x.EncryptedConnectionString).HasMaxLength(4000);
        builder.Property(x => x.EncryptedAdminConnectionString).HasMaxLength(4000);
        builder.Property(x => x.RuntimePrincipal).HasMaxLength(TenantDatabase.NameMaxLength).IsRequired();
        builder.Property(x => x.ConnectionStringName).HasMaxLength(TenantDatabase.ConnectionStringNameMaxLength);
        builder.Property(x => x.SchemaVersion).HasMaxLength(TenantDatabase.SchemaVersionMaxLength);
        builder.Property(x => x.LastMigrationError).HasMaxLength(TenantDatabase.ErrorMaxLength);

        builder.HasOne<DatabaseServer>().WithMany().HasForeignKey(x => x.DatabaseServerId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DatabaseServerConfiguration : IEntityTypeConfiguration<DatabaseServer>
{
    public void Configure(EntityTypeBuilder<DatabaseServer> builder)
    {
        builder.ToTable("DatabaseServers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(DatabaseServer.NameMaxLength).IsRequired();
        builder.HasIndex(x => x.Name).IsUnique();
        builder.Property(x => x.EncryptedAdminConnectionString).HasMaxLength(4000).IsRequired();
    }
}
