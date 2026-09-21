using Auth.Application.Common;
using Auth.Application.Common.Interfaces;
using Auth.Application.Constants;
using Auth.Application.Teams.Models;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Exceptions;
using NWFM.Shared.Results;

namespace Auth.Application.Teams.Commands.CreateTeam;

public sealed class CreateTeamCommandHandler(
    IAuthDbContext db,
    IUserAccountService accounts)
    : IRequestHandler<CreateTeamCommand, Result<TeamDto>>
{
    public async Task<Result<TeamDto>> Handle(CreateTeamCommand request, CancellationToken ct)
    {
        var name = request.Name.Trim();

        if (await db.Teams.AnyAsync(t => t.Name == name, ct))
            return Result.Failure<TeamDto>(AuthErrors.TeamDuplicateName);

        Team team;
        try
        {
            team = Team.Create(name, request.Mobile);
        }
        catch (DomainException ex)
        {
            return Result.Failure<TeamDto>(new Error("Auth.InvalidTeam", ex.Message));
        }

        db.Teams.Add(team);
        await db.SaveChangesAsync(ct);

        // The login can only be created once the team has an id to point at, so a rejected login — a
        // taken code, a password Identity refuses — would leave a team behind that cannot work. Undo
        // it rather than hand back a half-made crew someone has to clean up.
        var login = await accounts.CreateTeamLoginAsync(
            new NewTeamLogin
            {
                UserCode = request.UserCode,
                Email = request.Email,
                PhoneNumber = request.Mobile,
                Password = request.Password,
                TeamId = team.Id,
            },
            ct);

        if (login.IsFailure)
        {
            db.Teams.Remove(team);
            await db.SaveChangesAsync(ct);
            return Result.Failure<TeamDto>(login.Error);
        }

        var scopes = await OrgScopeRows.ReplaceAsync(
            db, OrgScopeOwnerTypes.Team, OrgScopeOwnerTypes.TeamOwnerId(team.Id), request.Scopes, ct);

        if (scopes.IsFailure)
            return Result.Failure<TeamDto>(scopes.Error);

        if (!request.IsActive)
        {
            team.Deactivate();
            await db.SaveChangesAsync(ct);
            await accounts.UpdateTeamLoginAsync(team.Id, request.Email, request.Mobile, isEnabled: false, ct);
        }

        var dtos = await TeamReader.ToDtosAsync(db, accounts, [team], ct);
        return Result.Success(dtos[0]);
    }
}
