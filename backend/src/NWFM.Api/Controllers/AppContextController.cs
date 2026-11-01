namespace NWFM.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.MultiTenancy;
using Workflow.Infrastructure.Persistence;

public sealed record TenantDto(Guid Id, string Key, string Name);
public sealed record ParticipantContextDto(Guid Id, Guid ActorId, string DisplayName, string? DisplayNameAr);
public sealed record AppContextDto(
    TenantDto Tenant,
    IReadOnlyList<ParticipantContextDto> Participants,
    Guid? DefaultParticipantId);

[ApiController]
[AllowAnonymous]
[Route("api/app-context")]
public sealed class AppContextController(
    ITenantContext tenants,
    WorkflowDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AppContextDto>> Get(CancellationToken ct)
    {
        var current = tenants.Current ?? throw TenantAccessException.Required();
        var tenant = new TenantDto(current.Id, current.Key, current.Name);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (User.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var userGuid))
            return new AppContextDto(tenant, [], null);

        var participants = await db.Participants
            .AsNoTracking()
            .Where(p => p.IsActive && p.UserId == userGuid)
            .Select(p => new ParticipantContextDto(p.Id, p.UserId, p.DisplayName, p.DisplayNameAr))
            .ToListAsync(ct);

        return new AppContextDto(tenant, participants, participants.FirstOrDefault()?.Id);
    }
}
