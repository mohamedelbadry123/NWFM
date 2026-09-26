using Auth.Application.Common;
using Auth.Application.Common.Interfaces;
using Auth.Application.Teams.Models;
using Auth.Domain.Constants;
using Auth.Domain.Entities;

namespace Auth.Application.Teams;

/// <summary>Builds <see cref="TeamDto"/>s: the team row, its login and its scopes, each read once per page.</summary>
internal static class TeamReader
{
    public static async Task<IReadOnlyList<TeamDto>> ToDtosAsync(
        IAuthDbContext db,
        IUserAccountService accounts,
        IReadOnlyList<Team> teams,
        CancellationToken ct)
    {
        var ids = teams.Select(t => t.Id).ToList();

        var logins = await accounts.GetTeamLoginsAsync(ids, ct);
        var scopes = await OrgScopeRows.ListManyAsync(
            db,
            OrgScopeOwnerTypes.Team,
            ids.Select(OrgScopeOwnerTypes.TeamOwnerId).ToList(),
            ct);

        return teams
            .Select(team =>
            {
                logins.TryGetValue(team.Id, out var login);
                scopes.TryGetValue(OrgScopeOwnerTypes.TeamOwnerId(team.Id), out var teamScopes);

                return new TeamDto
                {
                    Id = team.Id,
                    Name = team.Name,
                    Mobile = team.Mobile,
                    IsActive = team.IsActive,
                    UserCode = login?.UserCode,
                    Email = login?.Email,
                    LoginEnabled = login?.IsEnabled ?? false,
                    LastActiveAt = team.LastActiveAt,
                    CreatedAt = team.CreatedAt,
                    Scopes = teamScopes ?? [],
                };
            })
            .ToList();
    }
}
