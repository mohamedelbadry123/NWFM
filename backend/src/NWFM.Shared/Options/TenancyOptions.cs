using NWFM.Shared.MultiTenancy;

namespace NWFM.Shared.Options;

/// <summary>How requests, workers and the control plane find and reach tenants. Bound from <c>Tenancy</c>.</summary>
public sealed class TenancyOptions
{
    public const string SectionName = "Tenancy";

    /// <summary>
    /// Root domain tenant subdomains hang off: <c>example.com</c> makes <c>acme.example.com</c> resolve
    /// to tenant <c>acme</c>. Empty disables subdomain resolution (registered hosts only).
    /// </summary>
    public string RootDomain { get; set; } = string.Empty;

    /// <summary>
    /// Accept the <c>X-Tenant</c> header on anonymous calls. Development only: a deployed tenant is
    /// identified by its host, and a header any client can set is not evidence.
    /// </summary>
    public bool AllowTenantHeader { get; set; }

    /// <summary>Subdomains that can never become tenant keys.</summary>
    public string[] ReservedSubdomains { get; set; } =
    [
        "www", "api", "app", "admin", "platform", "console", "mail", "smtp", "support", "status",
        "static", "cdn", "assets", "auth", "login", "signup", "help", "docs", "dev", "uat", "localhost"
    ];

    /// <summary>Which tenant database credential this host uses.</summary>
    public TenantConnectionRole ConnectionRole { get; set; } = TenantConnectionRole.Runtime;

    public int SnapshotRefreshSeconds { get; set; } = 30;

    /// <summary>How many tenants a background worker processes at once.</summary>
    public int WorkerParallelism { get; set; } = 4;

    /// <summary>How long a passed database isolation verification is trusted before it is re-run.</summary>
    public int VerificationMinutes { get; set; } = 10;

    /// <summary>Migrate every active managed and bring-your-own tenant when the control plane starts.</summary>
    public bool MigrateTenantsOnStartup { get; set; }

    /// <summary>
    /// Tenants whose database is named in configuration rather than provisioned (the development
    /// workspace, e2e tenants, and adopted single-tenant deployments). Registered in the catalog by the
    /// control plane at startup; each host resolves <see cref="ConfigurationTenantOptions.ConnectionStringName"/>
    /// from its own configuration with its own credential.
    /// </summary>
    public List<ConfigurationTenantOptions> ConfigurationTenants { get; set; } = [];
}

public sealed class ConfigurationTenantOptions
{
    /// <summary>Must equal the tenant id already bound in the database, if any.</summary>
    public Guid Id { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Name under <c>ConnectionStrings</c>, e.g. <c>DefaultConnection</c>.</summary>
    public string ConnectionStringName { get; set; } = "DefaultConnection";

    /// <summary>The restricted request principal the control plane grants after migrating.</summary>
    public string RuntimePrincipal { get; set; } = "nwfm_request";

    /// <summary>Explicit media root; empty uses <c>{FileStorage:Root}/{Key}</c>.</summary>
    public string? StoragePath { get; set; }

    public string[] Hosts { get; set; } = [];

    public string ContactName { get; set; } = string.Empty;

    public string ContactEmail { get; set; } = string.Empty;

    public string PlanCode { get; set; } = "enterprise";

    public TenantSeedProfile SeedProfile { get; set; } = TenantSeedProfile.Bootstrap;

    public bool SsoEnabled { get; set; }
}
