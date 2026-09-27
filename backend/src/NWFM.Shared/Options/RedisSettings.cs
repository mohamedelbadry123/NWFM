namespace NWFM.Shared.Options;

/// <summary>One Redis server to connect to. Several entries describe a replica set or cluster.</summary>
public sealed class RedisEndpointSettings
{
    public const int DefaultPort = 6379;

    public string Server { get; set; } = string.Empty;

    public int Port { get; set; } = DefaultPort;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Server);

    public override string ToString() => $"{Server}:{Port}";
}

/// <summary>
/// Redis connection settings for the distributed cache layer.
/// </summary>
/// <remarks>
/// Deliberately a plain POCO: <c>NWFM.Shared</c> carries no caching or Redis package, so the
/// translation to the client's own connection options lives beside the registration in
/// Infrastructure. Only that one place knows which client backs the cache.
/// </remarks>
public sealed class RedisSettings
{
    /// <summary>Redis ACL user. "default" on a server that only has a password set.</summary>
    public string? Username { get; set; }

    /// <summary>Supplied per environment (env var or user secrets) — never committed.</summary>
    public string? Password { get; set; }

    public int ConnectRetry { get; set; } = 2;

    public int ConnectTimeoutMs { get; set; } = 5000;

    public bool Ssl { get; set; }

    /// <summary>
    /// False so a Redis that is down at startup does not stop the API booting; the client
    /// reconnects in the background and HybridCache serves from its in-process layer meanwhile.
    /// </summary>
    public bool AbortOnConnectFail { get; set; }

    public int Database { get; set; }

    /// <summary>Name this app reports on the Redis connection, so CLIENT LIST is readable.</summary>
    public string? ClientName { get; set; }

    public List<RedisEndpointSettings> EndPoints { get; set; } = [];

    public bool HasEndPoints => EndPoints.Exists(endpoint => endpoint.IsConfigured);
}
