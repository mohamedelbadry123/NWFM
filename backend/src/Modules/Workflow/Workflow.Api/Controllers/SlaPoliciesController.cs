namespace Workflow.Api.Controllers;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Results;
using Workflow.Application.Commands.CreateSlaPolicy;
using Workflow.Application.Commands.UpdateSlaPolicy;
using Workflow.Application.DTOs;
using Workflow.Application.Queries.GetSlaPolicyById;
using Workflow.Application.Queries.ListSlaPolicies;
using Workflow.Domain.Enums;

/// <summary>SuperAdmin CRUD for SLA policies.</summary>
[ApiController]
[Route("api/workflow/sla-policies")]
[Produces("application/json")]
public sealed class SlaPoliciesController : WorkflowControllerBase
{
    private readonly ISender _sender;
    public SlaPoliciesController(ISender sender) => _sender = sender;

    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResult<SlaPolicyDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new ListSlaPoliciesQuery(page, pageSize, search), cancellationToken);
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SlaPolicyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetSlaPolicyByIdQuery(id), cancellationToken);
        if (result.IsFailure && result.Error.Code.Contains("NotFound")) return NotFound(new { result.Error.Code, result.Error.Message });
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    [HttpPost]
    [ProducesResponseType(typeof(SlaPolicyDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        [FromBody] CreateSlaPolicyRequest body, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new CreateSlaPolicyCommand(
            body.PolicyCode, body.Name, body.Duration, body.DurationUnit, body.BusinessCalendarId,
            body.NameAr, body.OrganizationId, body.ReminderThresholdsJson,
            body.EscalationThresholdsJson, body.EscalationAssignmentKey), cancellationToken);
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(SlaPolicyDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateSlaPolicyRequest body, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new UpdateSlaPolicyCommand(
            id, body.Name, body.NameAr, body.Duration, body.DurationUnit, body.BusinessCalendarId,
            body.ReminderThresholdsJson, body.EscalationThresholdsJson,
            body.EscalationAssignmentKey, body.IsActive), cancellationToken);
        if (result.IsFailure && result.Error.Code.Contains("NotFound")) return NotFound(new { result.Error.Code, result.Error.Message });
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }
}

public sealed record CreateSlaPolicyRequest(
    string PolicyCode,
    string Name,
    int Duration,
    SlaDurationUnit DurationUnit,
    Guid BusinessCalendarId,
    string? NameAr = null,
    Guid? OrganizationId = null,
    string? ReminderThresholdsJson = null,
    string? EscalationThresholdsJson = null,
    string? EscalationAssignmentKey = null);

public sealed record UpdateSlaPolicyRequest(
    string Name,
    string? NameAr,
    int Duration,
    SlaDurationUnit DurationUnit,
    Guid BusinessCalendarId,
    string? ReminderThresholdsJson,
    string? EscalationThresholdsJson,
    string? EscalationAssignmentKey,
    bool IsActive);
