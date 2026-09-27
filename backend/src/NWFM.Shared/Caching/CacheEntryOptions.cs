namespace NWFM.Shared.Caching;

public sealed record CacheEntryOptions
{
    public TimeSpan Expiration { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan LocalExpiration { get; init; } = TimeSpan.FromMinutes(5);

    public static CacheEntryOptions FromMinutes(int minutes, int? localMinutes = null)
    {
        var expiration = TimeSpan.FromMinutes(Math.Max(minutes, 1));
        var local = localMinutes.HasValue
            ? TimeSpan.FromMinutes(Math.Max(localMinutes.Value, 1))
            : expiration;

        return new CacheEntryOptions
        {
            Expiration = expiration,
            LocalExpiration = local <= expiration ? local : expiration
        };
    }
}
