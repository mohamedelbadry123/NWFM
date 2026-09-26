using Auth.Application.Teams.Models;
using Auth.Application.Users.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace Auth.Application.Teams.Commands.CreateTeam;

/// <summary>
/// Raises a field team together with its login. A crew is a login as much as it is a record — one
/// without the other is a team that cannot work — so the two are made in one step.
/// </summary>
[Authorize(Policy = NwfmPolicies.ManageTeams)]
public sealed record CreateTeamCommand : IRequest<Result<TeamDto>>
{
    public string Name { get; init; } = default!;
    public string? Mobile { get; init; }

    /// <summary>The code the crew signs in with; becomes the login's user name.</summary>
    public string UserCode { get; init; } = default!;

    public string? Email { get; init; }
    public string Password { get; init; } = default!;
    public bool IsActive { get; init; } = true;

    /// <summary>
    /// Where the crew works. Required: login refuses a team with no scope, and a task is only
    /// assignable to a team whose territory covers it.
    /// </summary>
    public IReadOnlyList<OrgScopeAssignmentDto> Scopes { get; init; } = [];
}
