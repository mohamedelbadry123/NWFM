namespace Workflow.Api.Controllers;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Results;
using Workflow.Application.Commands.IgnoreWorkflowIncident;
using Workflow.Application.Commands.ResolveWorkflowIncident;
using Workflow.Application.DTOs;
using Workflow.Application.Queries.GetWorkflowIncidentById;
using Workflow.Application.Queries.ListInstanceIncidents;
using Workflow.Application.Queries.ListWorkflowIncidents;
using Workflow.Domain.Enums;

/// <summary>SuperAdmin incident console + org-safe instance incident summaries.</summary>
[ApiController]
[Route("api/workflow/incidents")]
[Produces("application/json")]
public sealed class WorkflowIncidentsController : WorkflowControllerBase
{
    private readonly ISender _sender;
    public WorkflowIncidentsController(ISender sender) => _sender = sender;

    private Guid GetOrgId() =>
        Context.OrganizationId;
    private Guid GetUserId() => Context.ActorId;

    /// <summary>SuperAdmin: paginated incident list across tenants.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResult<WorkflowIncidentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? organizationId = null,
        [FromQuery] WorkflowIncidentStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new ListWorkflowIncidentsQuery(page, pageSize, organizationId, status), cancellationToken);
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(WorkflowIncidentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetWorkflowIncidentByIdQuery(id), cancellationToken);
        if (result.IsFailure && result.Error.Code.Contains("NotFound")) return NotFound(new { result.Error.Code, result.Error.Message });
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    [HttpPost("{id:guid}/resolve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Resolve(
        Guid id, [FromBody] ResolveIncidentRequest? body, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new ResolveWorkflowIncidentCommand(id, GetUserId(), body?.Notes), cancellationToken);
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return NoContent();
    }

    [HttpPost("{id:guid}/ignore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Ignore(Guid id, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new IgnoreWorkflowIncidentCommand(id, GetUserId()), cancellationToken);
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return NoContent();
    }

    /// <summary>Org/Safe: incident summaries for an instance (no technical details).</summary>
    [HttpGet("by-instance/{instanceId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<WorkflowIncidentSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListByInstance(Guid instanceId, CancellationToken cancellationToken)
    {
        var isSuperAdmin = false;
        var result = await _sender.Send(
            new ListInstanceIncidentsQuery(instanceId, GetOrgId(), BypassTenantCheck: isSuperAdmin),
            cancellationToken);
        if (result.IsFailure && result.Error.Code.Contains("NotFound")) return NotFound(new { result.Error.Code, result.Error.Message });
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }
}

public sealed record ResolveIncidentRequest(string? Notes);
