namespace NWFM.Api.Services;

using System.Security.Claims;
using Auth.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Workflow.Infrastructure.Persistence;

public sealed class RequestContextMiddleware(RequestDelegate next, ILogger<RequestContextMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext http, ApplicationContext context, WorkflowDbContext db)
    {
        if (!http.Request.Path.StartsWithSegments("/api"))
        {
            await next(http);
            return;
        }

        if (context.OrganizationId == Guid.Empty)
            throw new InvalidOperationException("A server-configured tenant is required.");

        foreach (var header in new[] { "x-organization-id", "x-tenant-id" })
        {
            if (http.Request.Headers.TryGetValue(header, out var value) &&
                (value.Count != 1 || !Guid.TryParse(value[0], out var id) || id != context.OrganizationId))
            {
                await RejectSelector(http, context.OrganizationId);
                return;
            }
        }

        foreach (var key in new[] { "organizationId", "tenantId" })
        {
            if (http.Request.Query.TryGetValue(key, out var value) &&
                (value.Count != 1 || !Guid.TryParse(value[0], out var id) || id != context.OrganizationId))
            {
                await RejectSelector(http, context.OrganizationId);
                return;
            }
        }

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["TenantId"] = context.OrganizationId,
            ["TraceId"] = http.TraceIdentifier
        });
        if (http.User.Identity?.IsAuthenticated == true)
        {
            if (!Guid.TryParse(http.User.FindFirstValue(AppClaimTypes.TenantId), out var tokenTenant) ||
                tokenTenant != context.OrganizationId ||
                !Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userGuid))
            {
                logger.LogWarning(new EventId(4101, "TenantSessionDenied"),
                    "An authenticated request lacked verified tenant context.");
                http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await http.Response.WriteAsJsonAsync(new { code = "Tenant.Unauthorized", message = "Tenant access denied." });
                return;
            }
            var participant = await db.Participants
                .AsNoTracking()
                .Where(p => p.IsActive && p.UserId == userGuid)
                .FirstOrDefaultAsync(http.RequestAborted);

            // The authenticated identity may create definitions and instances before
            // the group-owning module supplies an assignment projection for it.
            context.SelectActor(userGuid, participant?.Id ?? Guid.Empty);
        }

        try
        {
            await next(http);
        }
        finally
        {
            context.SelectActor(Guid.Empty, Guid.Empty);
        }
    }

    private Task RejectSelector(HttpContext context, Guid tenantId)
    {
        context.Items["TenantSecurity.SelectorDenied"] = true;
        logger.LogWarning(new EventId(4102, "TenantSelectorDenied"),
            "Tenant selector rejected for deployment {TenantId}, request {TraceId}.",
            tenantId, context.TraceIdentifier);
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return context.Response.WriteAsJsonAsync(new { code = "Tenant.Mismatch", message = "The requested tenant is not the active tenant." });
    }
}
