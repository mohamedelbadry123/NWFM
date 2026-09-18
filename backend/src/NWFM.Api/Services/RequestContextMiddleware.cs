namespace NWFM.Api.Services;

using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Workflow.Infrastructure.Persistence;

public sealed class RequestContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, ApplicationContext context, WorkflowDbContext db)
    {
        if (!http.Request.Path.StartsWithSegments("/api"))
        {
            await next(http);
            return;
        }

        if (http.Request.Headers.TryGetValue("x-organization-id", out var tenant) &&
            (!Guid.TryParse(tenant, out var tenantId) || tenantId != context.OrganizationId))
        {
            await Reject(http, "Tenant.Mismatch", "The requested tenant is not the active tenant.");
            return;
        }

        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var userGuid))
        {
            var participant = await db.Participants
                .AsNoTracking()
                .Where(p => p.IsActive && p.UserId == userGuid)
                .FirstOrDefaultAsync(http.RequestAborted);

            if (participant is not null)
            {
                context.SelectActor(participant.UserId, participant.Id);
            }
        }

        await next(http);
    }

    private static Task Reject(HttpContext context, string code, string message)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return context.Response.WriteAsJsonAsync(new { code, message });
    }
}
