using NWFM.Shared.Options;
using StackExchange.Redis;

namespace Auth.Infrastructure.Caching;

/// <summary>
/// Translates <see cref="CacheSettings"/> into StackExchange.Redis connection options. The only
/// place in the solution that knows the cache is backed by Redis specifically — which is why the
/// settings themselves stay a plain POCO in NWFM.Shared.
/// </summary>
internal static class RedisConfigurationFactory
{
    private const string KeyPrefixSeparator = ":";

    /// <summary>
    /// Builds connection options from <see cref="CacheSettings.DistributedConnection"/> when set,
    /// otherwise from <see cref="CacheSettings.Redis"/>. Returns null when neither names a server,
    /// which the caller treats as "no Redis configured" rather than as an error.
    /// </summary>
    public static ConfigurationOptions? TryCreate(CacheSettings settings)
    {
        var redis = settings.Redis;

        if (!string.IsNullOrWhiteSpace(settings.DistributedConnection))
        {
            var parsed = ConfigurationOptions.Parse(settings.DistributedConnection);
            parsed.ClientName ??= redis.ClientName ?? settings.InstanceName;
            return parsed;
        }

        if (!redis.HasEndPoints)
            return null;

        var options = new ConfigurationOptions
        {
            User = string.IsNullOrWhiteSpace(redis.Username) ? null : redis.Username,
            Password = string.IsNullOrWhiteSpace(redis.Password) ? null : redis.Password,
            ConnectRetry = Math.Max(redis.ConnectRetry, 1),
            ConnectTimeout = Math.Max(redis.ConnectTimeoutMs, 1000),
            Ssl = redis.Ssl,
            AbortOnConnectFail = redis.AbortOnConnectFail,
            DefaultDatabase = redis.Database,
            ClientName = redis.ClientName ?? settings.InstanceName
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var endpoint in redis.EndPoints.Where(endpoint => endpoint.IsConfigured))
        {
            var server = endpoint.Server.Trim();

            if (!seen.Add($"{server}:{endpoint.Port}"))
                continue;

            try
            {
                options.EndPoints.Add(server, endpoint.Port);
            }
            catch (ArgumentException)
            {
                // One unusable entry must not stop the app from starting.
            }
        }

        return options.EndPoints.Count > 0 ? options : null;
    }

    /// <summary>
    /// Guarantees the instance prefix ends with the separator so Redis cache concatenation
    /// does not run key segments together.
    /// </summary>
    public static string NormalizeInstanceName(string? instanceName)
    {
        if (string.IsNullOrWhiteSpace(instanceName))
            return string.Empty;

        var trimmed = instanceName.Trim();

        return trimmed.EndsWith(KeyPrefixSeparator, StringComparison.Ordinal)
            ? trimmed
            : trimmed + KeyPrefixSeparator;
    }
}
