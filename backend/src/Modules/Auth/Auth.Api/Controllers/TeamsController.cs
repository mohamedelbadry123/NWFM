using Auth.Api.Common;
using Auth.Application.Teams.Commands.CreateTeam;
using Auth.Application.Teams.Commands.ResetTeamPassword;
using Auth.Application.Teams.Commands.SetTeamStatus;
using Auth.Application.Teams.Commands.UpdateTeam;
using Auth.Application.Teams.Models;
using Auth.Application.Teams.Queries.GetTeamById;
using Auth.Application.Teams.Queries.GetTeams;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;

namespace Auth.Api.Controllers;

/// <summary>Field teams and their logins.</summary>
[ApiController]
[Authorize]
[Route("api/v1/teams")]
public sealed class TeamsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = NwfmPolicies.TeamReaders)]
    [ProducesResponseType(typeof(Result<PaginatedResult<TeamDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTeams([FromQuery] GetTeamsQuery query, CancellationToken ct) =>
        (await sender.Send(query, ct)).ToActionResult();

    [HttpGet("{id:guid}")]
    [Authorize(Policy = NwfmPolicies.TeamReaders)]
    [ProducesResponseType(typeof(Result<TeamDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTeam(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetTeamByIdQuery(id), ct)).ToActionResult();

    [HttpPost]
    [Authorize(Policy = NwfmPolicies.ManageTeams)]
    [ProducesResponseType(typeof(Result<TeamDto>), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateTeam([FromBody] CreateTeamCommand command, CancellationToken ct) =>
        (await sender.Send(command, ct)).ToCreatedResult();

    [HttpPut("{id:guid}")]
    [Authorize(Policy = NwfmPolicies.ManageTeams)]
    [ProducesResponseType(typeof(Result<TeamDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTeam(Guid id, [FromBody] UpdateTeamCommand command, CancellationToken ct) =>
        (await sender.Send(command with { TeamId = id }, ct)).ToActionResult();

    [HttpPut("{id:guid}/status")]
    [Authorize(Policy = NwfmPolicies.ManageTeams)]
    [ProducesResponseType(typeof(Result<TeamDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] SetTeamStatusCommand command, CancellationToken ct) =>
        (await sender.Send(command with { TeamId = id }, ct)).ToActionResult();

    [HttpPost("{id:guid}/reset-password")]
    [Authorize(Policy = NwfmPolicies.ManageTeams)]
    [ProducesResponseType(typeof(Result<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetTeamPasswordCommand command, CancellationToken ct) =>
        (await sender.Send(command with { TeamId = id }, ct)).ToActionResult();
}
