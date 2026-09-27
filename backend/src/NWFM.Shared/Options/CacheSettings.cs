using NWFM.Shared.Caching;

namespace NWFM.Shared.Options;

/// <summary>Where cached values are stored. Switching provider is a configuration change only.</summary>
public enum CacheProviderType
{
    /// <summary>In-process cache only (single node).</summary>
    InMemory = 0,

    /// <summary>In-process cache backed by a distributed store shared across nodes.</summary>
    Distributed = 1
}

public sealed class CacheSettings
{
    public const string SectionName = "CacheSettings";

    /// <summary>In-memory today; flip to <see cref="CacheProviderType.Distributed"/> to add the shared L2 layer.</summary>
    public CacheProviderType Provider { get; set; } = CacheProviderType.InMemory;

    /// <summary>
    /// Key prefix applied by the distributed store so several apps can share one Redis instance.
    /// Must differ per deployment: PROD and UAT on the same host with identical values would collide.
    /// </summary>
    public string InstanceName { get; set; } = "nwfm";

    /// <summary>
    /// Raw Redis connection string. An escape hatch that takes precedence over <see cref="Redis"/>
    /// when set, for a connection that needs an option the structured settings do not expose.
    /// Ignored for <see cref="CacheProviderType.InMemory"/>.
    /// </summary>
    public string? DistributedConnection { get; set; }

    /// <summary>
    /// Structured Redis connection settings, used when <see cref="Provider"/> is
    /// <see cref="CacheProviderType.Distributed"/> and <see cref="DistributedConnection"/> is empty.
    /// With neither configured the registration falls back to an in-process distributed cache, so a
    /// half-configured environment starts rather than failing.
    /// </summary>
    public RedisSettings Redis { get; set; } = new();

    public int DefaultExpirationMinutes { get; set; } = 30;

    public int DefaultLocalExpirationMinutes { get; set; } = 5;

    /// <summary>Lifetime for reference data (departments, branches, lookups).</summary>
    public int LookupExpirationMinutes { get; set; } = 60;

    /// <summary>Upper bound for a single serialized entry.</summary>
    public long MaximumPayloadBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>
    /// Entry options for reference data. The in-process copy deliberately expires much sooner
    /// than the entry itself: once an L2 store is in play, eviction on one node cannot reach another
    /// node's L1, so a short local lifetime bounds how long a node can serve a stale lookup list.
    /// </summary>
    public CacheEntryOptions ToLookupEntryOptions() =>
        CacheEntryOptions.FromMinutes(LookupExpirationMinutes, DefaultLocalExpirationMinutes);
}
