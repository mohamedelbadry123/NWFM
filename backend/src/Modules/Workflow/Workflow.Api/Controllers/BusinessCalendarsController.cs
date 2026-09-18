namespace Workflow.Api.Controllers;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Results;
using Workflow.Application.Commands.AddBusinessCalendarHoliday;
using Workflow.Application.Commands.AddBusinessCalendarPeriod;
using Workflow.Application.Commands.CreateBusinessCalendar;
using Workflow.Application.Commands.UpdateBusinessCalendar;
using Workflow.Application.DTOs;
using Workflow.Application.Queries.GetBusinessCalendarById;
using Workflow.Application.Queries.ListBusinessCalendars;

/// <summary>SuperAdmin CRUD for business calendars used by SLA / timers.</summary>
[ApiController]
[Route("api/workflow/calendars")]
[Produces("application/json")]
public sealed class BusinessCalendarsController : WorkflowControllerBase
{
    private readonly ISender _sender;
    public BusinessCalendarsController(ISender sender) => _sender = sender;

    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResult<BusinessCalendarDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new ListBusinessCalendarsQuery(page, pageSize, search), cancellationToken);
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BusinessCalendarDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetBusinessCalendarByIdQuery(id), cancellationToken);
        if (result.IsFailure && result.Error.Code.Contains("NotFound")) return NotFound(new { result.Error.Code, result.Error.Message });
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    [HttpPost]
    [ProducesResponseType(typeof(BusinessCalendarDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        [FromBody] CreateBusinessCalendarRequest body, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new CreateBusinessCalendarCommand(
            body.Code, body.Name, body.TimeZone, body.NameAr, body.OrganizationId), cancellationToken);
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(BusinessCalendarDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateBusinessCalendarRequest body, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new UpdateBusinessCalendarCommand(
            id, body.Name, body.NameAr, body.TimeZone, body.IsActive), cancellationToken);
        if (result.IsFailure && result.Error.Code.Contains("NotFound")) return NotFound(new { result.Error.Code, result.Error.Message });
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    [HttpPost("{id:guid}/periods")]
    [ProducesResponseType(typeof(BusinessCalendarPeriodDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddPeriod(
        Guid id, [FromBody] AddCalendarPeriodRequest body, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new AddBusinessCalendarPeriodCommand(
            id, body.DayOfWeek, body.StartTime, body.EndTime, body.IsWorkingTime), cancellationToken);
        if (result.IsFailure && result.Error.Code.Contains("NotFound")) return NotFound(new { result.Error.Code, result.Error.Message });
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return CreatedAtAction(nameof(GetById), new { id }, result.Value);
    }

    [HttpPost("{id:guid}/holidays")]
    [ProducesResponseType(typeof(BusinessCalendarHolidayDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddHoliday(
        Guid id, [FromBody] AddCalendarHolidayRequest body, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new AddBusinessCalendarHolidayCommand(
            id, body.HolidayDate, body.Name, body.NameAr, body.IsRecurring), cancellationToken);
        if (result.IsFailure && result.Error.Code.Contains("NotFound")) return NotFound(new { result.Error.Code, result.Error.Message });
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return CreatedAtAction(nameof(GetById), new { id }, result.Value);
    }
}

public sealed record CreateBusinessCalendarRequest(
    string Code, string Name, string TimeZone, string? NameAr = null, Guid? OrganizationId = null);

public sealed record UpdateBusinessCalendarRequest(
    string Name, string? NameAr, string TimeZone, bool IsActive);

public sealed record AddCalendarPeriodRequest(
    DayOfWeek DayOfWeek, TimeSpan StartTime, TimeSpan EndTime, bool IsWorkingTime = true);

public sealed record AddCalendarHolidayRequest(
    DateOnly HolidayDate, string Name, string? NameAr = null, bool IsRecurring = false);
