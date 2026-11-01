using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Options;
using Tenancy.Application.Common.Interfaces;
using Tenancy.Infrastructure.Boundary;
using Tenancy.Infrastructure.MultiTenancy;
using Tenancy.Infrastructure.Persistence;
using Tenancy.Infrastructure.Security;
using Tenancy.Infrastructure.Services;

namespace Tenancy.Infrastructure;

/// <summary>
/// The part of the tenant catalog both hosts need at runtime: tenant resolution, connection strings,
/// the Data Protection key ring, support-access consent and the audit trail. Provisioning, platform
/// staff and catalog migrations belong to the control plane and are registered by
/// <c>Platform.Infrastructure</c> only.
/// </summary>
public static class DependencyInjection
{
    public const string CatalogConnectionName = "CatalogConnection";

    /// <summary>The assembly that owns the catalog's migrations: the control plane's.</summary>
    public const string CatalogMigrationsAssembly = "Platform.Infrastructure";

    private const string DataProtectionApplicationName = "NWFM";

    /// <param name="builder">The host.</param>
    /// <param name="role">
    /// Which tenant database credential this host may decrypt. Fixed by the host, never by
    /// configuration: the tenant API is <see cref="TenantConnectionRole.Runtime"/>, the control plane
    /// <see cref="TenantConnectionRole.Administration"/>.
    /// </param>
    public static IHostApplicationBuilder AddTenancyRuntime(this IHostApplicationBuilder builder, TenantConnectionRole role)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        services.AddOptions<TenancyOptions>()
            .Bind(configuration.GetSection(TenancyOptions.SectionName))
            .PostConfigure(options => options.ConnectionRole = role)
            .Validate(options => options.SnapshotRefreshSeconds is >= 5 and <= 600
                && options.WorkerParallelism is >= 1 and <= 64
                && options.VerificationMinutes is >= 1 and <= 1440, "Tenancy limits must be bounded and positive.")
            .ValidateOnStart();
        services.Configure<PlatformOptions>(configuration.GetSection(PlatformOptions.SectionName));

        // No fallback to DefaultConnection: the catalog holds every tenant's connection string, so it
        // must never quietly become a tenant's own database because a setting was missed.
        var catalogConnection = configuration.GetConnectionString(CatalogConnectionName);
        if (string.IsNullOrWhiteSpace(catalogConnection))
        {
            throw new InvalidOperationException($"ConnectionStrings:{CatalogConnectionName} is required.");
        }

        services.AddDbContext<CatalogDbContext>(options => options.UseSqlServer(
            catalogConnection,
            sql => sql
                .MigrationsHistoryTable(CatalogDbContext.MigrationsHistoryTable, CatalogDbContext.Schema)
                .MigrationsAssembly(CatalogMigrationsAssembly)));
        services.AddScoped<ICatalogDbContext>(sp => sp.GetRequiredService<CatalogDbContext>());

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ITenantContext, TenantContextAccessor>();
        services.AddScoped<ICurrentTenant, AmbientCurrentTenant>();
        services.AddSingleton<CatalogTenantStore>();
        services.AddSingleton<ITenantStore>(sp => sp.GetRequiredService<CatalogTenantStore>());
        services.AddSingleton<ITenantDatabaseCatalog>(sp => sp.GetRequiredService<CatalogTenantStore>());
        services.AddSingleton<ITenantConnectionStringResolver, TenantConnectionStringResolver>();
        services.AddSingleton<ITenantFanOut, TenantFanOut>();
        services.AddSingleton<TenantDatabaseVerifier>();

        // Both hosts see the same module set, so the schema the tenant API expects is exactly the
        // schema the control plane records after migrating.
        services.AddSingleton<ITenantDatabaseModule, TenantBoundaryModule>();
        services.AddSingleton<ITenantDatabaseModule, TenantRuntimeGrantsModule>();
        if (role == TenantConnectionRole.Runtime)
            services.AddSingleton<ITenantReadiness, TenantRuntimeReadiness>();

        // Both hosts share one key ring (same application name, same repository), so a secret the
        // control plane encrypts at provisioning is readable by the tenant API at request time. Only
        // the control plane creates and rotates keys.
        var dataProtection = services.AddDataProtection().SetApplicationName(DataProtectionApplicationName);
        if (role == TenantConnectionRole.Runtime)
            dataProtection.DisableAutomaticKeyGeneration();
        services.AddOptions<KeyManagementOptions>()
            .Configure<IServiceScopeFactory>((options, scopeFactory) => options.XmlRepository = new CatalogXmlRepository(scopeFactory));
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();

        services.AddScoped<IPlatformAuditWriter, PlatformAuditWriter>();
        services.AddScoped<ISupportSessionValidator, SupportSessionValidator>();

        // The tenant API has no platform identity at all; the control plane replaces this with the
        // reader of its own bearer scheme.
        services.TryAddScoped<IPlatformUser, AnonymousPlatformUser>();

        // The activation link is a password-reset token: the control plane generates it at
        // provisioning, the tenant API validates it at /auth/activate. Both must agree on its lifetime.
        services.AddOptions<DataProtectionTokenProviderOptions>()
            .Configure<IOptions<PlatformOptions>>((options, platform) =>
                options.TokenLifespan = TimeSpan.FromHours(platform.Value.AdminActivationHours));

        return builder;
    }
}
