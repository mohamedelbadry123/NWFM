using Auth.Application.Common.Interfaces;
using Auth.Application.Constants;
using Auth.Application.Teams.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace Auth.Application.Teams.Commands.SetTeamStatus;

/// <summary>
/// Activates or deactivates a team. Its login follows: an inactive crew cannot sign in, and cannot
/// be handed new work.
/// </summary>
[Authorize(Policy = NwfmPolicies.ManageTeams)]
public sealed record SetTeamStatusCommand : IRequest<Result<TeamDto>>
{
    public Guid TeamId { get; init; }
    public bool IsActive { get; init; }
}

public sealed class SetTeamStatusCommandHandler(
    IAuthDbContext db,
    IUserAccountService accounts)
    : IRequestHandler<SetTeamStatusCommand, Result<TeamDto>>
{
    public async Task<Result<TeamDto>> Handle(SetTeamStatusCommand request, CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == request.TeamId, ct);
        if (team is null)
            return Result.Failure<TeamDto>(AuthErrors.TeamNotFound);

        if (request.IsActive)
            team.Activate();
        else
            team.Deactivate();

        await db.SaveChangesAsync(ct);

        var logins = await accounts.GetTeamLoginsAsync([team.Id], ct);
        if (logins.TryGetValue(team.Id, out var login))
        {
            var updated = await accounts.UpdateTeamLoginAsync(team.Id, login.Email, login.PhoneNumber, request.IsActive, ct);
            if (updated.IsFailure)
                return Result.Failure<TeamDto>(updated.Error);
        }

        var dtos = await TeamReader.ToDtosAsync(db, accounts, [team], ct);
        return Result.Success(dtos[0]);
    }
}
