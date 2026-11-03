using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using NWFM.Shared.Abstractions;
using NWFM.Shared.MultiTenancy;

namespace NWFM.Api.Hosting;

public sealed class TenantResourceOptions
{
    /// <summary>Per tenant, unless the tenant's plan overrides it.</summary>
    public int RequestsPerMinute { get; set; } = 3600;
    public int PrincipalRequestsPerMinute { get; set; } = 300;
    public int LoginRequestsPerMinute { get; set; } = 20;

    /// <summary>Concurrent requests one tenant may hold.</summary>
    public int ConcurrentRequests { get; set; } = 32;

    /// <summary>Concurrent requests the whole process accepts before any tenant is known.</summary>
    public int GlobalConcurrentRequests { get; set; } = 256;

    /// <summary>Requests one client address may make per minute before any tenant is known.</summary>
    public int AddressRequestsPerMinute { get; set; } = 2400;
}

/// <summary>Request context items shared by the tenancy middlewares.</summary>
public static class TenantRequestItems
{
    /// <summary>The resolved tenant id, recorded so response callbacks can attribute a denial.</summary>
    public const string TenantId = "Tenancy.TenantId";

    public const string SelectorDenied = "TenantSecurity.SelectorDenied";

    public static Guid Of(HttpContext http) => http.Items.TryGetValue(TenantId, out var id) && id is Guid guid ? guid : Guid.Empty;
}

/// <summary>
/// The shared runtime serves many tenants. Process and per-address limits apply before a tenant is
/// known; request, concurrency and principal budgets are partitioned by tenant so one tenant cannot
/// exhaust another's allowance.
/// </summary>
public sealed class TenantResourceLimits : IDisposable
{
    private readonly TenantResourceOptions _limits;
    private readonly ConcurrencyLimiter _global;
    private readonly PartitionedRateLimiter<IPAddress> _addresses;
    private readonly PartitionedRateLimiter<Guid> _concurrency;
    private readonly PartitionedRateLimiter<TenantInfo> _requests;
    private readonly PartitionedRateLimiter<(Guid Tenant, HttpContext Http)> _principals;

    public TenantResourceLimits(IOptions<TenantResourceOptions> options)
    {
        var limits = _limits = options.Value;
        _global = new(new() { PermitLimit = limits.GlobalConcurrentRequests, QueueLimit = 0 });
        _addresses = PartitionedRateLimiter.Create<IPAddress, string>(address =>
            RateLimitPartition.GetFixedWindowLimiter(address.ToString(), _ => Window(limits.AddressRequestsPerMinute)));
        _concurrency = PartitionedRateLimiter.Create<Guid, Guid>(tenant =>
            RateLimitPartition.GetConcurrencyLimiter(tenant, _ => new() { PermitLimit = limits.ConcurrentRequests, QueueLimit = 0 }));
        _requests = PartitionedRateLimiter.Create<TenantInfo, Guid>(tenant =>
            RateLimitPartition.GetFixedWindowLimiter(tenant.Id, _ => Window(tenant.RequestsPerMinute ?? limits.RequestsPerMinute)));
        _principals = PartitionedRateLimiter.Create<(Guid Tenant, HttpContext Http), string>(request =>
        {
            var http = request.Http;
            var login = http.Request.Path.StartsWithSegments("/api/v1/auth") &&
                !http.Request.Path.Equals("/api/v1/auth/me", StringComparison.OrdinalIgnoreCase) &&
                !http.Request.Path.Equals("/api/v1/auth/sso/status", StringComparison.OrdinalIgnoreCase);
            var identity = login ? null : http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var key = request.Tenant.ToString("N") + ":" + (login ? "auth:" : "request:") +
                (identity is null ? "ip:" + http.Connection.RemoteIpAddress?.ToString() : "user:" + identity);
            return RateLimitPartition.GetFixedWindowLimiter(key,
                _ => Window(login ? limits.LoginRequestsPerMinute : limits.PrincipalRequestsPerMinute));
        });
    }

    public RateLimitLease AcquireGlobal() => _global.AttemptAcquire();

    public RateLimitLease AcquireAddress(IPAddress? address) => _addresses.AttemptAcquire(address ?? IPAddress.None);

    public RateLimitLease AcquireConcurrency(TenantInfo tenant) => _concurrency.AttemptAcquire(tenant.Id);

    public RateLimitLease AcquireRequest(TenantInfo tenant) => _requests.AttemptAcquire(tenant);

    public RateLimitLease AcquirePrincipal(Guid tenantId, HttpContext http) => _principals.AttemptAcquire((tenantId, http));

    public void Dispose()
    {
        _principals.Dispose();
        _requests.Dispose();
        _concurrency.Dispose();
        _addresses.Dispose();
        _global.Dispose();
    }

    private static FixedWindowRateLimiterOptions Window(int permits) => new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true
    };
}

/// <summary>Bounds work before any tenant is known and records every denial against the tenant it was resolved to.</summary>
public sealed class GlobalRequestBudgetMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, TenantResourceLimits limits, TenantSecuritySignals signals)
    {
        if (!http.Request.Path.StartsWithSegments("/api"))
        {
            await next(http);
            return;
        }
        // Authenticated responses and session material must not enter shared HTTP caches.
        http.Response.Headers.CacheControl = "no-store";
        http.Response.OnStarting(() =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (http.Response.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden
                or StatusCodes.Status429TooManyRequests || http.Items.ContainsKey(TenantRequestItems.SelectorDenied))
                signals.Denied(TenantRequestItems.Of(http), http.Response.StatusCode);
            return Task.CompletedTask;
        });
        using var global = limits.AcquireGlobal();
        using var address = limits.AcquireAddress(http.Connection.RemoteIpAddress);
        if (!global.IsAcquired || !address.IsAcquired)
        {
            await TenantRequestBudgetMiddleware.RejectAsync(http, !global.IsAcquired ? global : address);
            return;
        }
        await next(http);
    }
}

/// <summary>After resolution: the tenant's own request and concurrency budget, and its kill switch.</summary>
public sealed class TenantRequestBudgetMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, TenantResourceLimits limits, ITenantContext tenants,
        ITenantAvailability availability)
    {
        if (!http.Request.Path.StartsWithSegments("/api"))
        {
            await next(http);
            return;
        }
        var tenant = tenants.Current ?? throw TenantAccessException.Required();
        using var concurrency = limits.AcquireConcurrency(tenant);
        using var requests = limits.AcquireRequest(tenant);
        if (!concurrency.IsAcquired || !requests.IsAcquired)
        {
            await RejectAsync(http, !concurrency.IsAcquired ? concurrency : requests);
            return;
        }
        // The tenant's own database flag is read uncached, so an operator deactivation is immediate.
        if (!await availability.IsActiveAsync(http.RequestAborted))
        {
            http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await http.Response.WriteAsJsonAsync(new { code = TenancyErrorCodes.TenantInactive, message = "The tenant is not active." });
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
    public async Task InvokeAsync(HttpContext http, TenantResourceLimits limits, ITenantContext tenants)
    {
        if (!http.Request.Path.StartsWithSegments("/api"))
        {
            await next(http);
            return;
        }
        var tenant = tenants.Current ?? throw TenantAccessException.Required();
        using var lease = limits.AcquirePrincipal(tenant.Id, http);
        if (!lease.IsAcquired)
        {
            await TenantRequestBudgetMiddleware.RejectAsync(http, lease);
            return;
        }
        await next(http);
    }
}
