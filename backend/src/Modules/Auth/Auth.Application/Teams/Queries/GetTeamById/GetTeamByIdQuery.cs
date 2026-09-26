using Auth.Application.Common.Interfaces;
using Auth.Application.Constants;
using Auth.Application.Teams.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace Auth.Application.Teams.Queries.GetTeamById;

[Authorize(Policy = NwfmPolicies.TeamReaders)]
public sealed record GetTeamByIdQuery(Guid TeamId) : IRequest<Result<TeamDto>>;

public sealed class GetTeamByIdQueryHandler(
    IAuthDbContext db,
    IUserAccountService accounts)
    : IRequestHandler<GetTeamByIdQuery, Result<TeamDto>>
{
    public async Task<Result<TeamDto>> Handle(GetTeamByIdQuery request, CancellationToken ct)
    {
        var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == request.TeamId, ct);
        if (team is null)
            return Result.Failure<TeamDto>(AuthErrors.TeamNotFound);

        var dtos = await TeamReader.ToDtosAsync(db, accounts, [team], ct);
        return Result.Success(dtos[0]);
    }
}
