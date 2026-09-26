using Auth.Application.Teams.Models;
using Auth.Application.Users.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace Auth.Application.Teams.Commands.UpdateTeam;

/// <summary>
/// Edits a team's name, contact details and territory. The login's code is fixed once created — the
/// crew's devices sign in with it — so it is not part of this command.
/// </summary>
[Authorize(Policy = NwfmPolicies.ManageTeams)]
public sealed record UpdateTeamCommand : IRequest<Result<TeamDto>>
{
    public Guid TeamId { get; init; }
    public string Name { get; init; } = default!;
    public string? Mobile { get; init; }
    public string? Email { get; init; }
    public IReadOnlyList<OrgScopeAssignmentDto> Scopes { get; init; } = [];
}
