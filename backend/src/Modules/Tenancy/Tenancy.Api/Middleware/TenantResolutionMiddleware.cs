using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NWFM.Shared.Http;
using NWFM.Shared.MultiTenancy;
using NWFM.Shared.Options;

namespace Tenancy.Api.Middleware;

/// <summary>
/// Binds every request to exactly one tenant, or deliberately to none, before any handler runs, so
/// the scoped module DbContexts are built against the right database. Fails closed: a request that is
/// tenant-scoped but cannot be bound is refused here, never served from a default.
/// </summary>
/// <remarks>
/// Order of evidence: the signed <c>tid</c> claim (already bound by <see cref="TenantSessionBinder"/>
/// during authentication); otherwise the host (a registered host or <c>{key}.{RootDomain}</c>);
/// otherwise, in development only and only for anonymous calls, the <c>X-Tenant</c> header; then, for
/// the anonymous refresh call, the refresh token's tenant prefix. A host or header that names another
/// tenant than the token is a mismatch. A token never selects a tenant through the header: with a
/// bearer present the header is ignored.
/// </remarks>
public sealed class TenantResolutionMiddleware(
    RequestDelegate next,
    ITenantContext tenantContext,
    ITenantStore tenantStore,
    IOptions<TenancyOptions> tenancyOptions,
    ILogger<TenantResolutionMiddleware> logger)
{
    /// <summary>Request item holding the resolved tenant id, read by response callbacks that attribute denials.</summary>
    public const string TenantIdItem = "Tenancy.TenantId";

    private const int MaxPeekBytes = 16 * 1024;

    public async Task InvokeAsync(HttpContext context)
    {
        if (TenantRoutes.IsPlatformScoped(context.Request.Path))
        {
            if (!tenantContext.IsResolved) tenantContext.MarkPlatformScope();
            await next(context);
            return;
        }

        TenantInfo tenant;
        try
        {
            tenant = await ResolveAsync(context);
            if (TenantAccessException.ForStatus(tenant) is { } status)
                throw status;
            foreach (var gate in context.RequestServices.GetServices<ITenantReadiness>())
                if (await gate.CheckAsync(tenant, context.RequestAborted) is { } refusal)
                    throw refusal;
        }
        catch (TenantAccessException refusal)
        {
            await WriteRefusalAsync(context, refusal);
            return;
        }

        tenantContext.SetTenant(tenant);
        context.Items[TenantIdItem] = tenant.Id;
        await next(context);
    }

    private async Task<TenantInfo> ResolveAsync(HttpContext context)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            var tenant = tenantContext.Current ?? await BindFromClaimsAsync(context, user);
            var host = tenantStore.FindByHost(context.Request.Host.Host);
            if (host is not null && host.Id != tenant.Id)
                throw TenantAccessException.Mismatch();
            await EnsureSupportSessionAsync(context, user, tenant);
            return tenant;
        }

        // An anonymous request whose bearer was refused during authentication keeps that refusal.
        if (context.Items.TryGetValue(TenantClaimTypes.TenantId + ".Refusal", out var refused) && refused is TenantAccessException earlier
            && earlier.Code != TenancyErrorCodes.TenantRequired)
            throw earlier;

        return ResolveHint(context)
            ?? await ResolveRefreshAsync(context)
            ?? throw TenantAccessException.Required();
    }

    private static async Task<TenantInfo> BindFromClaimsAsync(HttpContext context, ClaimsPrincipal user)
    {
        var binder = context.RequestServices.GetRequiredService<ITenantSessionBinder>();
        if (await binder.BindAsync(user, context.RequestAborted) is { } refusal)
            throw refusal;
        return context.RequestServices.GetRequiredService<ITenantContext>().Current!;
    }

    private TenantInfo? ResolveHint(HttpContext context)
    {
        var fromHost = tenantStore.FindByHost(context.Request.Host.Host);
        TenantInfo? fromHeader = null;

        if (tenancyOptions.Value.AllowTenantHeader
            && !context.Request.Headers.ContainsKey("Authorization")
            && context.Request.Headers.TryGetValue(TenantHttpHeaders.Tenant, out var header)
            && !string.IsNullOrWhiteSpace(header))
        {
            fromHeader = tenantStore.FindByKey(header.ToString()) ?? throw TenantAccessException.NotFound();
        }

        if (fromHost is not null && fromHeader is not null && fromHost.Id != fromHeader.Id)
            throw TenantAccessException.Mismatch();

        return fromHost ?? fromHeader;
    }

    private static async Task EnsureSupportSessionAsync(HttpContext context, ClaimsPrincipal user, TenantInfo tenant)
    {
        var sessionClaim = user.FindFirst(TenantClaimTypes.SupportSessionId)?.Value;
        if (sessionClaim is null) return;

        var validator = context.RequestServices.GetRequiredService<ISupportSessionValidator>();
        if (!Guid.TryParse(sessionClaim, out var sessionId)
            || !await validator.IsActiveAsync(sessionId, tenant.Id, context.RequestAborted))
        {
            throw new TenantAccessException(
                "The support session has ended or its access was revoked.",
                TenancyErrorCodes.SupportAccessNotGranted,
                StatusCodes.Status401Unauthorized);
        }
    }

    /// <summary>The refresh call is anonymous and may come from a host that names no tenant; its token can.</summary>
    private async Task<TenantInfo?> ResolveRefreshAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method)
            || !context.Request.Path.Equals(TenantRoutes.Refresh, StringComparison.OrdinalIgnoreCase))
            return null;

        var refreshToken = await PeekStringAsync(context, TenantRoutes.RefreshTokenField);
        return TenantRefreshTokens.TryReadTenantId(refreshToken, out var tenantId)
            ? tenantStore.FindById(tenantId) ?? throw TenantAccessException.NotFound()
            : null;
    }

    /// <summary>Reads one top-level string from a small JSON body and rewinds it for model binding.</summary>
    private static async Task<string?> PeekStringAsync(HttpContext context, string propertyName)
    {
        var request = context.Request;
        if (request.ContentLength is > MaxPeekBytes
            || request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
            return null;

        request.EnableBuffering(MaxPeekBytes);
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: context.RequestAborted);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var property in document.RootElement.EnumerateObject())
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            return null;
        }
        catch (JsonException)
        {
            // Left to model binding to reject with its usual validation response.
            return null;
        }
        finally
        {
            request.Body.Position = 0;
        }
    }

    private async Task WriteRefusalAsync(HttpContext context, TenantAccessException refusal)
    {
        var host = context.Request.Host.Host;
        logger.LogWarning(new EventId(4203, "TenantResolutionRefused"),
            "Tenant resolution refused {Path} on host {Host}: {Code}", context.Request.Path, host, refusal.Code);
        context.RequestServices.GetService<ITenantRefusalSink>()?
            .ResolutionRefused(tenantContext.Current?.Id ?? Guid.Empty, host, refusal.Code);

        context.Response.StatusCode = refusal.StatusCode;
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(
            ResultActionResults.FailureBody(refusal.Code, refusal.Message), context.RequestAborted);
    }
}

/// <summary>Request-scoped ambient tenant holder; must wrap authentication so the bearer handler can bind it.</summary>
public sealed class TenantRequestScopeMiddleware(RequestDelegate next, ITenantContext tenantContext)
{
    public async Task InvokeAsync(HttpContext context)
    {
        using var scope = tenantContext.BeginRequestScope();
        await next(context);
    }
}

public static class TenantResolutionMiddlewareExtensions
{
    /// <summary>Before authentication: opens the request's tenant holder.</summary>
    public static IApplicationBuilder UseTenantRequestScope(this IApplicationBuilder app) =>
        app.UseMiddleware<TenantRequestScopeMiddleware>();

    /// <summary>After authentication (the <c>tid</c> claim) and before authorization (which reads tenant data).</summary>
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app) =>
        app.UseMiddleware<TenantResolutionMiddleware>();
}
