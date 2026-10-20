using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using NWFM.Api.Services;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Options;

namespace NWFM.Api.Hosting;

public sealed class TenantResourceOptions
{
    public int RequestsPerMinute { get; set; } = 3600;
    public int PrincipalRequestsPerMinute { get; set; } = 300;
    public int LoginRequestsPerMinute { get; set; } = 20;
    public int ConcurrentRequests { get; set; } = 32;
}

/// <summary>
/// Each deployment has a dedicated database, workers and tenant. These limits
/// bound the request workload of that deployment; cluster quotas remain deployment controls.
/// </summary>
public sealed class TenantResourceLimits : IDisposable
{
    private readonly ConcurrencyLimiter _concurrency;
    private readonly FixedWindowRateLimiter _requests;
    private readonly PartitionedRateLimiter<HttpContext> _principals;

    public TenantResourceLimits(IOptions<TenantResourceOptions> options, IOptions<ApplicationOptions> application)
    {
        var limits = options.Value;
        var tenant = application.Value.TenantId.ToString("N");
        _concurrency = new(new() { PermitLimit = limits.ConcurrentRequests, QueueLimit = 0 });
        _requests = new(new()
        {
            PermitLimit = limits.RequestsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
        _principals = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        {
            var login = http.Request.Path.StartsWithSegments("/api/v1/auth") &&
                !http.Request.Path.Equals("/api/v1/auth/me", StringComparison.OrdinalIgnoreCase) &&
                !http.Request.Path.Equals("/api/v1/auth/sso/status", StringComparison.OrdinalIgnoreCase);
            var identity = login ? null : http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var key = tenant + ":" + (login ? "auth:" : "request:") +
                (identity is null ? "ip:" + http.Connection.RemoteIpAddress?.ToString() : "user:" + identity);
            return RateLimitPartition.GetFixedWindowLimiter(key, _ => new()
            {
                PermitLimit = login ? limits.LoginRequestsPerMinute : limits.PrincipalRequestsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
        });
    }

    public RateLimitLease AcquireConcurrency() => _concurrency.AttemptAcquire();
    public RateLimitLease AcquireRequest() => _requests.AttemptAcquire();
    public RateLimitLease AcquirePrincipal(HttpContext http) => _principals.AttemptAcquire(http);

    public void Dispose()
    {
        _principals.Dispose();
        _requests.Dispose();
        _concurrency.Dispose();
    }
}

/// <summary>Bounds work before authentication and records verified deployment context.</summary>
public sealed class TenantRequestBudgetMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, TenantResourceLimits limits,
        IOptions<ApplicationOptions> application, IOptionsMonitor<TenantIsolationOptions> tenant,
        ITenantAvailability availability, TenantSecuritySignals signals,
        ILogger<TenantRequestBudgetMiddleware> logger)
    {
        if (!http.Request.Path.StartsWithSegments("/api"))
        {
            await next(http);
            return;
        }
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["TenantId"] = application.Value.TenantId,
            ["TraceId"] = http.TraceIdentifier
        });
        // Authenticated responses and session material must not enter shared HTTP caches.
        http.Response.Headers.CacheControl = "no-store";
        http.Response.OnStarting(() =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (http.Response.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden
                or StatusCodes.Status429TooManyRequests || http.Items.ContainsKey("TenantSecurity.SelectorDenied"))
                signals.Denied(http.Response.StatusCode);
            return Task.CompletedTask;
        });
        using var concurrency = limits.AcquireConcurrency();
        using var requests = limits.AcquireRequest();
        if (!concurrency.IsAcquired || !requests.IsAcquired)
        {
            await RejectAsync(http, !concurrency.IsAcquired ? concurrency : requests);
            return;
        }
        if (!tenant.CurrentValue.IsActive || !await availability.IsActiveAsync(http.RequestAborted))
        {
            http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await http.Response.WriteAsJsonAsync(new { code = "Tenant.Inactive", message = "The tenant is not active." });
            return;
        }
        await next(http);
    }

    internal static Task RejectAsync(HttpContext http, RateLimitLease lease)
    {
        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        var retry = lease.TryGetMetadata(MetadataName.RetryAfter, out var delay)
            ? Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds)) : 1;
        http.Response.Headers.RetryAfter = retry.ToString(CultureInfo.InvariantCulture);
        return http.Response.WriteAsJsonAsync(new { code = "Tenant.RateLimit", message = "Request limit exceeded." });
    }
}

public sealed class TenantPrincipalBudgetMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, TenantResourceLimits limits)
    {
        if (!http.Request.Path.StartsWithSegments("/api"))
        {
            await next(http);
            return;
        }
        using var lease = limits.AcquirePrincipal(http);
        if (!lease.IsAcquired)
        {
            await TenantRequestBudgetMiddleware.RejectAsync(http, lease);
            return;
        }
        await next(http);
    }
}
