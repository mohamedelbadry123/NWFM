namespace Workflow.Api.Controllers;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Workflow.Application.Commands.DeactivateParticipant;
using Workflow.Application.Commands.RegisterParticipant;
using Workflow.Application.DTOs;
using Workflow.Application.Queries.GetPagedParticipants;
using Workflow.Application.Queries.GetParticipantById;
using NWFM.Shared.Results;

/// <summary>Manages the Workflow participant directory for the active tenant.</summary>
[ApiController]
[Route("api/workflow/participants")]
[Produces("application/json")]
public sealed class WorkflowParticipantsController : WorkflowControllerBase
{
    private readonly ISender _sender;

    public WorkflowParticipantsController(ISender sender) => _sender = sender;

    private bool TryGetOrganizationId(out Guid orgId)
        => TryResolveTenant(out orgId);

    /// <summary>Returns a paginated list of workflow participants for the active tenant.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResult<WorkflowParticipantDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out var orgId)) return Forbid();

        var result = await _sender.Send(
            new GetPagedParticipantsQuery(orgId, page, pageSize, search), cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.ModuleDisabled")
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>Returns a single workflow participant by ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(WorkflowParticipantDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out var orgId)) return Forbid();

        var result = await _sender.Send(new GetParticipantByIdQuery(id, orgId), cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Participant.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>
    /// Creates a workflow participant with a stable actor identifier in the active tenant.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(WorkflowParticipantDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterParticipantRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out var orgId)) return Forbid();

        var command = new RegisterParticipantCommand(orgId, request.DisplayName, request.Email, request.DisplayNameAr, request.EmployeeNumber);
        var result = await _sender.Send(command, cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Participant.AlreadyRegistered")
            return Conflict(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure && result.Error.Code == "Workflow.Participant.UserNotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>Deactivates a workflow participant. Does not delete the record.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out var orgId)) return Forbid();

        var participant = await _sender.Send(new GetParticipantByIdQuery(id, orgId), cancellationToken);
        if (participant.IsSuccess && participant.Value.UserId == Context.DefaultActorId)
            return Conflict(new { Code = "Participant.DefaultActor", Message = "The default participant cannot be deactivated. Configure another default participant first." });

        var result = await _sender.Send(new DeactivateParticipantCommand(id, orgId), cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Participant.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return NoContent();
    }
}

public sealed record RegisterParticipantRequest(string DisplayName, string Email, string? DisplayNameAr = null, string? EmployeeNumber = null);
