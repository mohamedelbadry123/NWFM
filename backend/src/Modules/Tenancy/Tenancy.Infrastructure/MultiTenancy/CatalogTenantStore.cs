using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NWFM.Shared.Options;
using Tenancy.Domain.Entities;
using Tenancy.Infrastructure.Persistence;

namespace Tenancy.Infrastructure.MultiTenancy;

/// <summary>Connection material for one tenant. Internal: never leaves Infrastructure.</summary>
internal sealed record TenantDatabaseRecord(
    TenantDatabaseMode Mode,
    string? EncryptedConnectionString,
    string? EncryptedAdminConnectionString,
    string? ConnectionStringName,
    string RuntimePrincipal,
    DateTime? VerifiedAtUtc);

internal interface ITenantDatabaseCatalog
{
    TenantDatabaseRecord? FindDatabase(Guid tenantId);
}

/// <summary>
/// The catalog held in memory and reloaded every <see cref="TenancyOptions.SnapshotRefreshSeconds"/>.
/// One caller reloads while the rest keep reading the previous snapshot, so a slow or briefly
/// unreachable catalog never stalls requests for tenants already known.
/// </summary>
internal sealed class CatalogTenantStore(
    IServiceScopeFactory scopeFactory,
    IOptions<TenancyOptions> tenancyOptions,
    TimeProvider timeProvider,
    ILogger<CatalogTenantStore> logger)
    : ITenantStore, ITenantDatabaseCatalog
{
    private readonly Lock _gate = new();
    private Snapshot? _snapshot;

    public TenantInfo? FindById(Guid id) => Current.ById.GetValueOrDefault(id);

    public TenantInfo? FindByKey(string key) =>
        string.IsNullOrWhiteSpace(key) ? null : Current.ByKey.GetValueOrDefault(TenantKey.Normalize(key));

    public TenantInfo? FindByHost(string host)
    {
        var normalized = TenantHost.Normalize(host);
        if (normalized.Length == 0)
        {
            return null;
        }

        var snapshot = Current;
        if (snapshot.ByHost.TryGetValue(normalized, out var mapped))
        {
            return mapped;
        }

        var options = tenancyOptions.Value;
        var rootDomain = TenantHost.Normalize(options.RootDomain);
        if (rootDomain.Length == 0 || !normalized.EndsWith("." + rootDomain, StringComparison.Ordinal))
        {
            return null;
        }

        var label = normalized[..^(rootDomain.Length + 1)];
        if (label.Contains('.') || options.ReservedSubdomains.Contains(label, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        return snapshot.ByKey.GetValueOrDefault(label);
    }

    public IReadOnlyList<TenantInfo> GetAll() => Current.All;

    public TenantDatabaseRecord? FindDatabase(Guid tenantId) => Current.Databases.GetValueOrDefault(tenantId);

    public void Invalidate()
    {
        lock (_gate)
        {
            _snapshot = null;
        }
    }

    private Snapshot Current
    {
        get
        {
            var snapshot = Volatile.Read(ref _snapshot);
            var now = timeProvider.GetUtcNow();

            if (snapshot is not null && snapshot.ExpiresAt > now)
            {
                return snapshot;
            }

            if (snapshot is null)
            {
                lock (_gate)
                {
                    return _snapshot ??= Load();
                }
            }

            // Stale: one caller refreshes; everybody else keeps the previous snapshot meanwhile.
            if (_gate.TryEnter())
            {
                try
                {
                    if (_snapshot is { } current && current.ExpiresAt <= now)
                    {
                        _snapshot = Load();
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Refreshing the tenant catalog failed; serving the previous snapshot.");
                    _snapshot = snapshot with { ExpiresAt = now.AddSeconds(RefreshSeconds) };
                }
                finally
                {
                    _gate.Exit();
                }
            }

            return Volatile.Read(ref _snapshot) ?? snapshot;
        }
    }

    private int RefreshSeconds => Math.Max(5, tenancyOptions.Value.SnapshotRefreshSeconds);

    private Snapshot Load()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var now = timeProvider.GetUtcNow();

        var tenants = db.Tenants
            .AsNoTracking()
            .AsSplitQuery()
            .Include(t => t.Hosts)
            .Include(t => t.Subscription!).ThenInclude(s => s.Plan)
            .Where(t => t.Status != TenantStatus.Deleted)
            .ToList();

        // Projected rather than included: the tenant API never selects the owner credential, so its
        // catalog login can be denied that column outright (scripts/provision-catalog-reader.sql).
        var administration = tenancyOptions.Value.ConnectionRole == TenantConnectionRole.Administration;
        var databases = (administration
                ? db.TenantDatabases.AsNoTracking().Select(d => new TenantDatabaseRow(d.TenantId, d.Mode, d.EncryptedConnectionString,
                    d.EncryptedAdminConnectionString, d.ConnectionStringName, d.RuntimePrincipal, d.VerifiedAtUtc, d.SchemaVersion))
                : db.TenantDatabases.AsNoTracking().Select(d => new TenantDatabaseRow(d.TenantId, d.Mode, d.EncryptedConnectionString,
                    null, d.ConnectionStringName, d.RuntimePrincipal, d.VerifiedAtUtc, d.SchemaVersion)))
            .ToList()
            .ToDictionary(d => d.TenantId);

        var byId = new Dictionary<Guid, TenantInfo>(tenants.Count);
        var byKey = new Dictionary<string, TenantInfo>(tenants.Count, StringComparer.Ordinal);
        var byHost = new Dictionary<string, TenantInfo>(StringComparer.Ordinal);
        var records = new Dictionary<Guid, TenantDatabaseRecord>(tenants.Count);

        foreach (var tenant in tenants)
        {
            var database = databases.GetValueOrDefault(tenant.Id);
            var info = ToInfo(tenant, database?.SchemaVersion, now.UtcDateTime);
            byId[info.Id] = info;
            byKey[info.Key] = info;

            foreach (var host in tenant.Hosts)
            {
                byHost[host.Host] = info;
            }

            if (database is not null)
            {
                records[tenant.Id] = new TenantDatabaseRecord(database.Mode, database.EncryptedConnectionString,
                    database.EncryptedAdminConnectionString, database.ConnectionStringName, database.RuntimePrincipal, database.VerifiedAtUtc);
            }
        }


        return new Snapshot(byId, byKey, byHost, records, [.. byId.Values], now.AddSeconds(RefreshSeconds));
    }

    private static TenantInfo ToInfo(Tenant tenant, string? schemaVersion, DateTime utcNow)
    {
        var subscription = tenant.Subscription;
        var plan = subscription?.Plan;

        return new TenantInfo
        {
            Id = tenant.Id,
            Key = tenant.Key,
            Name = tenant.Name,
            Status = tenant.Status,
            StoragePath = tenant.StoragePath,
            IsSubscriptionUsable = subscription?.IsUsable(utcNow) ?? false,
            PlanCode = plan?.Code,
            MaxUsers = plan?.MaxUsers,
            SsoEnabled = tenant.Settings.SsoEnabled,
            IsListedPublicly = tenant.Settings.ListedPublicly,
            SchemaVersion = schemaVersion
        };
    }

    private sealed record TenantDatabaseRow(
        Guid TenantId,
        TenantDatabaseMode Mode,
        string? EncryptedConnectionString,
        string? EncryptedAdminConnectionString,
        string? ConnectionStringName,
        string RuntimePrincipal,
        DateTime? VerifiedAtUtc,
        string? SchemaVersion);

    private sealed record Snapshot(
        Dictionary<Guid, TenantInfo> ById,
        Dictionary<string, TenantInfo> ByKey,
        Dictionary<string, TenantInfo> ByHost,
        Dictionary<Guid, TenantDatabaseRecord> Databases,
        IReadOnlyList<TenantInfo> All,
        DateTimeOffset ExpiresAt);
}
