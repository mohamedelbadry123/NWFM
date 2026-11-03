namespace Platform.Application.Tenants.Models;

public sealed record PlatformTenantListItemDto
{
    public Guid Id { get; init; }

    public string Key { get; init; } = default!;

    public string Name { get; init; } = default!;

    public string Status { get; init; } = default!;

    public string? StatusReason { get; init; }

    public string ContactEmail { get; init; } = default!;

    public string? PlanCode { get; init; }

    public string? SubscriptionStatus { get; init; }

    public DateTime? TrialEndsAtUtc { get; init; }

    public DateTime? PeriodEndsAtUtc { get; init; }

    public string? DatabaseMode { get; init; }

    public string? SchemaVersion { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}

public sealed record PlatformTenantDetailDto
{
    public Guid Id { get; init; }

    public string Key { get; init; } = default!;

    public string Name { get; init; } = default!;

    public string Status { get; init; } = default!;

    public string? StatusReason { get; init; }

    public string ContactName { get; init; } = default!;

    public string ContactEmail { get; init; } = default!;

    public string? ContactPhone { get; init; }

    public string SeedProfile { get; init; } = default!;

    public string? StoragePath { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime? ActivatedAtUtc { get; init; }

    public IReadOnlyList<string> Hosts { get; init; } = [];

    public PlatformTenantSettingsDto Settings { get; init; } = default!;

    public PlatformTenantDatabaseDto? Database { get; init; }

    public PlatformTenantSubscriptionDto? Subscription { get; init; }
}

public sealed record PlatformTenantSettingsDto
{
    public bool SsoEnabled { get; init; }

    public bool ListedPublicly { get; init; }

    public string? SsoEntityId { get; init; }

    public string? SsoIdentityProviderEntityId { get; init; }

    public string? SsoMetadataUrl { get; init; }

    public string DefaultLanguage { get; init; } = default!;
}

/// <summary>Where the tenant's data lives. Never carries the connection string or its secret.</summary>
public sealed record PlatformTenantDatabaseDto
{
    public string Mode { get; init; } = default!;

    public string? DatabaseName { get; init; }

    public int? DatabaseServerId { get; init; }

    public string? ConnectionStringName { get; init; }

    public string? SchemaVersion { get; init; }

    public DateTime? LastMigratedAtUtc { get; init; }

    /// <summary>When the runtime credential last passed the isolation verification.</summary>
    public DateTime? VerifiedAtUtc { get; init; }

    public string? RuntimePrincipal { get; init; }

    public string? LastMigrationError { get; init; }
}

public sealed record PlatformTenantSubscriptionDto
{
    public Guid Id { get; init; }

    public string PlanCode { get; init; } = default!;

    public string PlanNameEn { get; init; } = default!;

    public string PlanNameAr { get; init; } = default!;

    public string Status { get; init; } = default!;

    public DateTime StartedAtUtc { get; init; }

    public DateTime? TrialEndsAtUtc { get; init; }

    public DateTime? PeriodEndsAtUtc { get; init; }

    public string? Notes { get; init; }

    public int? MaxUsers { get; init; }

    public int? MaxTeams { get; init; }

    public int? MaxStorageGb { get; init; }
}
