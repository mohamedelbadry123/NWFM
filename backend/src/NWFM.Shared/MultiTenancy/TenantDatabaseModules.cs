namespace NWFM.Shared.MultiTenancy;

/// <summary>Which seed set a tenant database receives.</summary>
public enum TenantSeedProfile
{
    /// <summary>Roles, permissions, the role matrix and the example forms and task types.</summary>
    Standard = 1,

    /// <summary>The development workspace: the standard seed plus a default administrator and demo data.</summary>
    Bootstrap = 2
}

/// <summary>
/// A module that owns objects in every tenant database. The control plane migrates and seeds each
/// registered module in <see cref="Order"/> inside the tenant's scope; the tenant API compares their
/// migration ids with the recorded version and refuses a database that is behind.
/// </summary>
/// <remarks>
/// Registered as a singleton. The work methods take a service provider from a scope opened after the
/// tenant was bound, so the module's DbContext connects to that tenant.
/// </remarks>
public interface ITenantDatabaseModule
{
    /// <summary>Stable name used in the composite schema version, e.g. <c>Auth</c>.</summary>
    string Name { get; }

    /// <summary>Lower runs first. The boundary is 0; modules whose seeds depend on others run later.</summary>
    int Order { get; }

    /// <summary>The newest migration this build carries for the module; null when it has none.</summary>
    string? LatestMigrationId { get; }

    Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken);

    Task SeedAsync(IServiceProvider services, TenantSeedProfile profile, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetPendingMigrationsAsync(IServiceProvider services, CancellationToken cancellationToken);
}

/// <summary>
/// The tenant schema version: one <c>Module=MigrationId</c> pair per module, joined with <c>;</c>.
/// The control plane records it after migrating; the tenant API gates requests on it.
/// </summary>
public static class TenantSchema
{
    private const char PairSeparator = ';';
    private const char ValueSeparator = '=';

    public static string? Compose(IEnumerable<ITenantDatabaseModule> modules)
    {
        var pairs = modules
            .Where(m => m.LatestMigrationId is not null)
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .Select(m => $"{m.Name}{ValueSeparator}{m.LatestMigrationId}")
            .ToList();
        return pairs.Count == 0 ? null : string.Join(PairSeparator, pairs);
    }

    /// <summary>
    /// True when any module's recorded migration is older than this build expects. Migration ids sort
    /// by their timestamp prefix. A database ahead of the build is served: migrations are additive and
    /// refusing it would block a rollback.
    /// </summary>
    public static bool IsBehind(string? recordedVersion, IEnumerable<ITenantDatabaseModule> modules)
    {
        var recorded = Parse(recordedVersion);
        foreach (var module in modules)
        {
            if (module.LatestMigrationId is not { } expected) continue;
            if (!recorded.TryGetValue(module.Name, out var applied) || string.CompareOrdinal(applied, expected) < 0)
                return true;
        }
        return false;
    }

    public static IReadOnlyDictionary<string, string> Parse(string? version)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(version)) return result;
        foreach (var pair in version.Split(PairSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var index = pair.IndexOf(ValueSeparator);
            if (index > 0) result[pair[..index]] = pair[(index + 1)..];
        }
        return result;
    }
}
