namespace NWFM.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NWFM.Api.Services;
using Workflow.Infrastructure.Persistence;

public sealed record TenantDto(Guid Id, string Name);
public sealed record ParticipantContextDto(Guid Id, Guid ActorId, string DisplayName, string? DisplayNameAr);
public sealed record AppContextDto(TenantDto Tenant, Guid DefaultParticipantId, IReadOnlyList<ParticipantContextDto> Participants);

[ApiController]
[Route("api/app-context")]
public sealed class AppContextController(WorkflowDbContext db, IOptions<ApplicationOptions> options) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AppContextDto>> Get(CancellationToken ct)
    {
        var participants = await db.Participants.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.DisplayName)
            .Select(p => new ParticipantContextDto(p.Id, p.UserId, p.DisplayName, p.DisplayNameAr)).ToListAsync(ct);
        return new AppContextDto(new(options.Value.TenantId, options.Value.TenantName),
            participants.Single(p => p.ActorId == options.Value.DefaultActorId).Id, participants);
    }
}
