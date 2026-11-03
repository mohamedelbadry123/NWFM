using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using NWFM.Shared.MultiTenancy;

namespace Platform.Api.Middleware;

/// <summary>
/// Marks every request as platform scope. The control plane never binds a tenant to a request; work
/// against one tenant opens its own tenant scope explicitly (provisioning, migrations, support).
/// </summary>
public sealed class PlatformScopeMiddleware(RequestDelegate next, ITenantContext tenantContext)
{
    public async Task InvokeAsync(HttpContext context)
    {
        using var requestScope = tenantContext.BeginRequestScope();
        tenantContext.MarkPlatformScope();
        await next(context);
    }
}

public static class PlatformScopeMiddlewareExtensions
{
    public static IApplicationBuilder UsePlatformScope(this IApplicationBuilder app) =>
        app.UseMiddleware<PlatformScopeMiddleware>();
}
