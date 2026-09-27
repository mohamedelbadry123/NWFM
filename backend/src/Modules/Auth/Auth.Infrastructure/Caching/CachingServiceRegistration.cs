using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NWFM.Shared.Caching;
using NWFM.Shared.Options;
using StackExchange.Redis;

namespace Auth.Infrastructure.Caching;

/// <summary>
/// Registers the single caching stack used by the whole application.
/// This is the only place that knows which provider backs <see cref="ICacheService"/>.
/// </summary>
public static class CachingServiceRegistration
{
    public static IServiceCollection AddCachingServices(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(CacheSettings.SectionName);
        services.Configure<CacheSettings>(section);

        var settings = section.Get<CacheSettings>() ?? new CacheSettings();

        // L1 — always present, in-process.
        services.AddMemoryCache();

        // L2 — optional. HybridCache picks up any registered IDistributedCache automatically,
        // so switching the whole app to a shared cache happens here and nowhere else.
        if (settings.Provider == CacheProviderType.Distributed)
        {
            var redisOptions = RedisConfigurationFactory.TryCreate(settings);

            if (redisOptions is not null)
            {
                // One multiplexer shared by the cache and the health check, connected on first use
                // rather than here: a Redis that is slow or down must never block application
                // startup. AbortOnConnectFail defaults to false so the client keeps retrying in the
                // background instead of throwing, and HybridCache serves from L1 in the meantime.
                var multiplexer = new Lazy<IConnectionMultiplexer>(
                    () => ConnectionMultiplexer.Connect(redisOptions),
                    LazyThreadSafetyMode.ExecutionAndPublication);

                services.AddSingleton(_ => multiplexer.Value);

                services.AddStackExchangeRedisCache(options =>
                {
                    options.ConnectionMultiplexerFactory = () => Task.FromResult(multiplexer.Value);

                    // The prefix is concatenated onto the key verbatim, so it has to carry its own
                    // separator or it runs into the root from CacheKeys.
                    options.InstanceName = RedisConfigurationFactory.NormalizeInstanceName(settings.InstanceName);
                });

                services.AddHealthChecks()
                    .AddCheck<RedisCacheHealthCheck>(
                        RedisCacheHealthCheck.Name,
                        failureStatus: HealthStatus.Degraded,
                        tags: ["cache", "redis"]);
            }
            else
            {
                // Distributed was asked for but no server was named. An in-process distributed cache
                // keeps the L2 code path exercised — and keeps a half-configured environment
                // starting — instead of failing at boot over a cache.
                services.AddDistributedMemoryCache();
            }
        }

        services.AddHybridCache(options =>
        {
            options.MaximumPayloadBytes = settings.MaximumPayloadBytes;
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(Math.Max(settings.DefaultExpirationMinutes, 1)),
                LocalCacheExpiration = TimeSpan.FromMinutes(Math.Max(settings.DefaultLocalExpirationMinutes, 1))
            };
        });

        services.AddSingleton<ICacheService, HybridCacheService>();

        return services;
    }
}
