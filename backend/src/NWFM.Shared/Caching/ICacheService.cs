namespace NWFM.Shared.Caching;

public interface ICacheService
{
    ValueTask<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        CacheEntryOptions? options = null,
        CancellationToken cancellationToken = default);

    ValueTask SetAsync<T>(
        string key,
        T value,
        CacheEntryOptions? options = null,
        CancellationToken cancellationToken = default);

    ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default);

    ValueTask RemoveAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);
}
