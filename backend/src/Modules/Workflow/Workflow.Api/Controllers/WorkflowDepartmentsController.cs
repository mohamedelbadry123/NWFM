namespace Workflow.Api.Controllers;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Workflow.Application.Commands.AddDepartmentMember;
using Workflow.Application.Commands.CreateWorkflowDepartment;
using Workflow.Application.Commands.RemoveDepartmentMember;
using Workflow.Application.Commands.UpdateWorkflowDepartment;
using Workflow.Application.DTOs;
using Workflow.Application.Queries.GetPagedDepartments;
using NWFM.Shared.Results;

/// <summary>Manages workflow departments for the active tenant.</summary>
[ApiController]
[Route("api/workflow/departments")]
[Produces("application/json")]
public sealed class WorkflowDepartmentsController : WorkflowControllerBase
{
    private readonly ISender _sender;

    public WorkflowDepartmentsController(ISender sender) => _sender = sender;

    private bool TryGetOrganizationId(out Guid orgId)
        => TryResolveTenant(out orgId);

    /// <summary>Returns a paginated list of workflow departments for the active tenant.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResult<WorkflowDepartmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out var orgId)) return Forbid();

        var result = await _sender.Send(
            new GetPagedDepartmentsQuery(orgId, page, pageSize, search), cancellationToken);

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>Creates a new workflow department.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(WorkflowDepartmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        [FromBody] CreateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out var orgId)) return Forbid();

        var command = new CreateWorkflowDepartmentCommand(
            orgId, request.Name, request.NameAr, request.Code, request.DefaultAssignmentGroupId);
        var result = await _sender.Send(command, cancellationToken);

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>Updates a workflow department.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(WorkflowDepartmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out var orgId)) return Forbid();

        var command = new UpdateWorkflowDepartmentCommand(
            id, orgId, request.Name, request.NameAr, request.Code, request.DefaultAssignmentGroupId);
        var result = await _sender.Send(command, cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Department.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>Adds a participant to a department.</summary>
    [HttpPost("{id:guid}/members")]
    [ProducesResponseType(typeof(WorkflowDepartmentMemberDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AddMember(
        Guid id,
        [FromBody] AddDepartmentMemberRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out var orgId)) return Forbid();

        var command = new AddDepartmentMemberCommand(id, orgId, request.ParticipantId);
        var result = await _sender.Send(command, cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Department.MemberAlreadyExists")
            return Conflict(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>Removes a participant from a department.</summary>
    [HttpDelete("{id:guid}/members/{participantId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RemoveMember(
        Guid id,
        Guid participantId,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out var orgId)) return Forbid();

        var result = await _sender.Send(
            new RemoveDepartmentMemberCommand(id, orgId, participantId), cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Department.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure && result.Error.Code == "Workflow.Department.MemberNotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return NoContent();
    }
}

public sealed record CreateDepartmentRequest(
    string Name,
    string? NameAr,
    string? Code,
    Guid? DefaultAssignmentGroupId);

public sealed record UpdateDepartmentRequest(
    string Name,
    string? NameAr,
    string? Code,
    Guid? DefaultAssignmentGroupId);

public sealed record AddDepartmentMemberRequest(Guid ParticipantId);
