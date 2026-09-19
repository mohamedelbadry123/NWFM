namespace NWFM.Api.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Workflow.Infrastructure.Persistence;

public sealed class RequestContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, ApplicationContext context, WorkflowDbContext db, IOptions<ApplicationOptions> options)
    {
        if (!http.Request.Path.StartsWithSegments("/api")) { await next(http); return; }
        if (http.Request.Headers.TryGetValue("x-organization-id", out var tenant) &&
            (!Guid.TryParse(tenant, out var tenantId) || tenantId != context.OrganizationId))
        {
            await Reject(http, "Tenant.Mismatch", "The requested tenant is not the active tenant."); return;
        }
        // Callback identity comes from its connection's API key or signature,
        // not from the selected interactive participant.
        if (http.Request.Path.StartsWithSegments("/api/workflow/integrations/webhooks")) { await next(http); return; }
        var participants = db.Participants.AsNoTracking().Where(p => p.IsActive);
        if (http.Request.Headers.TryGetValue("X-Workflow-Participant-Id", out var actor))
        {
            if (!Guid.TryParse(actor, out var participantId)) { await Reject(http, "Participant.Invalid", "A valid participant ID is required."); return; }
            participants = participants.Where(p => p.Id == participantId);
        }
        else participants = participants.Where(p => p.UserId == options.Value.DefaultActorId);

        var participant = await participants.FirstOrDefaultAsync(http.RequestAborted);
        if (participant is null) { await Reject(http, "Participant.Unavailable", "The selected or default participant is unavailable."); return; }
        context.SelectActor(participant.UserId, participant.Id);
        await next(http);
    }

    private static Task Reject(HttpContext context, string code, string message)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return context.Response.WriteAsJsonAsync(new { code, message });
    }
}
