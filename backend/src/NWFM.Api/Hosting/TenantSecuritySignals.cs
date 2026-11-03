using System.Collections.Concurrent;
using NWFM.Shared.MultiTenancy;

namespace NWFM.Api.Hosting;

/// <summary>
/// Bounded per-tenant denial monitoring; collectors route the alert event. Denials that happen
/// before a tenant is resolved (unknown host, refused resolution) share one "unresolved" window.
/// </summary>
public sealed class TenantSecuritySignals(TimeProvider clock, ILogger<TenantSecuritySignals> logger) : ITenantRefusalSink
{
    private const int Threshold = 20;
    private const int MaxTrackedTenants = 10_000;
    private readonly ConcurrentDictionary<Guid, Window> _windows = new();

    /// <summary>A request bound to <paramref name="tenantId"/> (or unresolved when empty) was denied.</summary>
    public void Denied(Guid tenantId, int statusCode)
    {
        logger.LogWarning(new EventId(4201, "TenantAccessDenied"),
            "Tenant security denial: TenantId={TenantId}, StatusCode={StatusCode}.",
            Label(tenantId), statusCode);
        Count(tenantId);
    }

    /// <summary>A request could not be bound to a serving tenant (unknown, mismatched or not serving).</summary>
    public void ResolutionRefused(Guid tenantId, string? tenantKey, string code)
    {
        logger.LogWarning(new EventId(4203, "TenantResolutionRefused"),
            "Tenant resolution refused: TenantId={TenantId}, TenantKey={TenantKey}, Code={Code}.",
            Label(tenantId), tenantKey ?? "-", code);
        Count(tenantId);
    }

    private void Count(Guid tenantId)
    {
        if (_windows.Count > MaxTrackedTenants) Evict();
        var window = _windows.GetOrAdd(tenantId, _ => new Window());
        if (window.Record(clock.GetUtcNow()))
            logger.LogError(new EventId(4202, "TenantIsolationAlert"),
                "Review repeated security denials: TenantId={TenantId}, Threshold={Threshold}, WindowSeconds=60.",
                Label(tenantId), Threshold);
    }

    private void Evict()
    {
        var cutoff = clock.GetUtcNow().AddMinutes(-1);
        foreach (var (key, window) in _windows)
            if (window.LastSeen <= cutoff) _windows.TryRemove(key, out _);
    }

    private static string Label(Guid tenantId) => tenantId == Guid.Empty ? "unresolved" : tenantId.ToString("D");

    private sealed class Window
    {
        private readonly Queue<DateTimeOffset> _denials = new();
        private DateTimeOffset? _lastAlert;

        public DateTimeOffset LastSeen { get; private set; }

        /// <summary>Records a denial; true when it completes a burst that has not alerted within the last minute.</summary>
        public bool Record(DateTimeOffset now)
        {
            lock (_denials)
            {
                LastSeen = now;
                while (_denials.TryPeek(out var oldest) && oldest <= now.AddMinutes(-1))
                    _denials.Dequeue();
                if (_denials.Count == Threshold) _denials.Dequeue();
                _denials.Enqueue(now);
                if (_denials.Count < Threshold || _lastAlert is { } last && last > now.AddMinutes(-1))
                    return false;
                _lastAlert = now;
                return true;
            }
        }
    }
}
