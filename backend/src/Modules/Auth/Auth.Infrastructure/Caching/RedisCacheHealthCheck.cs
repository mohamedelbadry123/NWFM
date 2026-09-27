using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Auth.Infrastructure.Caching;

/// <summary>
/// Pings the Redis backing the cache's L2.
/// </summary>
/// <remarks>
/// Reports Degraded rather than Unhealthy on failure: HybridCache keeps its in-process L1, so a
/// Redis outage makes the app slower and its caches node-local — it does not stop it serving.
/// Registered only when Redis is actually configured, so a green check means a real connection.
/// </remarks>
internal sealed class RedisCacheHealthCheck(IConnectionMultiplexer multiplexer) : IHealthCheck
{
    public const string Name = "redis-cache";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var started = Stopwatch.GetTimestamp();
            await multiplexer.GetDatabase().PingAsync();
            var elapsed = Stopwatch.GetElapsedTime(started);

            return HealthCheckResult.Healthy(
                $"Redis responded in {elapsed.TotalMilliseconds:F0} ms.",
                new Dictionary<string, object> { ["latencyMs"] = elapsed.TotalMilliseconds });
        }
        catch (Exception exception)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                "Redis is unreachable; the cache is serving from the in-process layer only.",
                exception);
        }
    }
}
