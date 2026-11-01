namespace NWFM.Shared.MultiTenancy;

/// <summary>
/// A tenant as read from the catalog snapshot. Carries no secrets: connection strings are only
/// resolved inside infrastructure by <see cref="ITenantConnectionStringResolver"/>.
/// </summary>
public sealed class TenantInfo
{
    /// <summary>Catalog id; also the <c>OrganizationId</c> of every row and the database boundary binding.</summary>
    public required Guid Id { get; init; }

    /// <summary>The tenant's slug; also its default subdomain.</summary>
    public required string Key { get; init; }

    public required string Name { get; init; }

    public required TenantStatus Status { get; init; }

    /// <summary>Explicit media root. Null means <c>{FileStorage:Root}/{Key}</c>.</summary>
    public string? StoragePath { get; init; }

    /// <summary>False when the subscription is suspended, cancelled, expired or past its trial.</summary>
    public bool IsSubscriptionUsable { get; init; } = true;

    public string? PlanCode { get; init; }

    public int? MaxUsers { get; init; }

    /// <summary>Plan override for the tenant request budget; null uses the configured default.</summary>
    public int? RequestsPerMinute { get; init; }

    public bool SsoEnabled { get; init; }

    /// <summary>The tenant opted in to appearing on the public landing page's workspace list.</summary>
    public bool IsListedPublicly { get; init; }

    /// <summary>The composite schema version the control plane last applied; null if never recorded.</summary>
    public string? SchemaVersion { get; init; }

    public bool IsServing => Status == TenantStatus.Active && IsSubscriptionUsable;
}
