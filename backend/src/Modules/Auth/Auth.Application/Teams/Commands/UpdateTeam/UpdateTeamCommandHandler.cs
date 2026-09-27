using Auth.Application.Common;
using Auth.Application.Common.Interfaces;
using Auth.Application.Constants;
using Auth.Application.Teams.Models;
using Auth.Domain.Constants;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Exceptions;
using NWFM.Shared.Results;

namespace Auth.Application.Teams.Commands.UpdateTeam;

public sealed class UpdateTeamCommandHandler(
    IAuthDbContext db,
    IUserAccountService accounts)
    : IRequestHandler<UpdateTeamCommand, Result<TeamDto>>
{
    public async Task<Result<TeamDto>> Handle(UpdateTeamCommand request, CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == request.TeamId, ct);
        if (team is null)
            return Result.Failure<TeamDto>(AuthErrors.TeamNotFound);

        var name = request.Name.Trim();
        if (await db.Teams.AnyAsync(t => t.Id != team.Id && t.Name == name, ct))
            return Result.Failure<TeamDto>(AuthErrors.TeamDuplicateName);

        try
        {
            team.Update(name, request.Mobile);
        }
        catch (DomainException ex)
        {
            return Result.Failure<TeamDto>(new Error("Auth.InvalidTeam", ex.Message));
        }

        await db.SaveChangesAsync(ct);

        var scopes = await OrgScopeRows.ReplaceAsync(
            db, OrgScopeOwnerTypes.Team, OrgScopeOwnerTypes.TeamOwnerId(team.Id), request.Scopes, ct);

        if (scopes.IsFailure)
            return Result.Failure<TeamDto>(scopes.Error);

        // A team created before it had a login has nothing to update here, and that is not an error.
        var logins = await accounts.GetTeamLoginsAsync([team.Id], ct);
        if (logins.ContainsKey(team.Id))
        {
            var login = await accounts.UpdateTeamLoginAsync(team.Id, request.Email, request.Mobile, team.IsActive, ct);
            if (login.IsFailure)
                return Result.Failure<TeamDto>(login.Error);
        }

        var dtos = await TeamReader.ToDtosAsync(db, accounts, [team], ct);
        return Result.Success(dtos[0]);
    }
}
