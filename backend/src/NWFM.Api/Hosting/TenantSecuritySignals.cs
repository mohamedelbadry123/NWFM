using Microsoft.Extensions.Options;
using NWFM.Api.Services;

namespace NWFM.Api.Hosting;

/// <summary>Bounded per-deployment denial monitoring; collectors route the alert event.</summary>
public sealed class TenantSecuritySignals(
    IOptions<ApplicationOptions> application,
    TimeProvider clock,
    ILogger<TenantSecuritySignals> logger)
{
    private readonly Queue<DateTimeOffset> _denials = new();
    private readonly object _gate = new();
    private DateTimeOffset? _lastAlert;

    public void Denied(int statusCode)
    {
        var now = clock.GetUtcNow();
        var alert = false;
        lock (_gate)
        {
            while (_denials.TryPeek(out var oldest) && oldest <= now.AddMinutes(-1))
                _denials.Dequeue();
            if (_denials.Count == 20) _denials.Dequeue();
            _denials.Enqueue(now);
            if (_denials.Count == 20 && (_lastAlert is null || _lastAlert <= now.AddMinutes(-1)))
            {
                _lastAlert = now;
                alert = true;
            }
        }
        logger.LogWarning(new EventId(4201, "TenantAccessDenied"),
            "Tenant security denial: TenantId={TenantId}, StatusCode={StatusCode}.",
            application.Value.TenantId, statusCode);
        if (alert)
            logger.LogError(new EventId(4202, "TenantIsolationAlert"),
                "Review repeated security denials: TenantId={TenantId}, Threshold=20, WindowSeconds=60.",
                application.Value.TenantId);
    }
}
