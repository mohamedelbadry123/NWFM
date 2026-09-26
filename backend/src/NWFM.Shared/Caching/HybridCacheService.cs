using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using NWFM.Shared.Options;

namespace NWFM.Shared.Caching;

/// <summary>
/// <see cref="ICacheService"/> over <see cref="HybridCache"/>.
/// HybridCache always keeps an in-process (L1) copy and transparently uses whatever
/// <c>IDistributedCache</c> is registered as L2 — so moving from in-memory to distributed
/// is a registration change in <see cref="CachingServiceRegistration"/>, not a code change here.
/// </summary>
public sealed class HybridCacheService(HybridCache cache, IOptions<CacheSettings> settings) : ICacheService
{
    private readonly CacheEntryOptions _defaults = CacheEntryOptions.FromMinutes(
        settings.Value.DefaultExpirationMinutes,
        settings.Value.DefaultLocalExpirationMinutes);

    public ValueTask<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        CacheEntryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        cache.GetOrCreateAsync(
            key,
            factory,
            static (state, token) => state(token),
            ToHybridOptions(options),
            tags: null,
            cancellationToken);

    public ValueTask SetAsync<T>(
        string key,
        T value,
        CacheEntryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        cache.SetAsync(key, value, ToHybridOptions(options), tags: null, cancellationToken);

    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        cache.RemoveAsync(key, cancellationToken);

    public ValueTask RemoveAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default) =>
        cache.RemoveAsync(keys, cancellationToken);

    private HybridCacheEntryOptions ToHybridOptions(CacheEntryOptions? options)
    {
        var effective = options ?? _defaults;

        return new HybridCacheEntryOptions
        {
            Expiration = effective.Expiration,
            LocalCacheExpiration = effective.LocalExpiration
        };
    }
}
