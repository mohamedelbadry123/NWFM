namespace Workflow.Api.Controllers;

using NWFM.Shared.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Workflow.Application.Commands.AddGroupMember;
using Workflow.Application.Commands.CreateAssignmentGroup;
using Workflow.Application.Commands.RemoveGroupMember;
using Workflow.Application.Commands.UpdateAssignmentGroup;
using Workflow.Application.DTOs;
using Workflow.Application.Queries.GetAssignmentGroupById;
using Workflow.Application.Queries.GetPagedAssignmentGroups;
using Workflow.Domain.Enums;
using NWFM.Shared.Results;

/// <summary>Manages workflow assignment groups for the active tenant.</summary>
[ApiController]
[Route("api/workflow/assignment-groups")]
[Produces("application/json")]
[Authorize(Policy = NwfmPolicies.ManageGroups)]
public sealed class WorkflowAssignmentGroupsController : WorkflowControllerBase
{
    private readonly ISender _sender;

    public WorkflowAssignmentGroupsController(ISender sender) => _sender = sender;

    private bool TryGetOrganizationId(out Guid orgId)
        => TryResolveTenant(out orgId);

    /// <summary>Returns a paginated list of assignment groups for the active tenant.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResult<WorkflowAssignmentGroupDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out var orgId)) return Forbid();

        var result = await _sender.Send(
            new GetPagedAssignmentGroupsQuery(orgId, page, pageSize, search), cancellationToken);

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>Returns a single assignment group with its member list.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(WorkflowAssignmentGroupDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out var orgId)) return Forbid();

        var result = await _sender.Send(new GetAssignmentGroupByIdQuery(id, orgId), cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.AssignmentGroup.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>Creates a new assignment group.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(WorkflowAssignmentGroupDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        [FromBody] CreateAssignmentGroupRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out var orgId)) return Forbid();

        var command = new CreateAssignmentGroupCommand(
            orgId,
            request.Name,
            request.NameAr,
            request.AssignmentStrategy,
            string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim().ToUpperInvariant());
        var result = await _sender.Send(command, cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.AssignmentGroup.DuplicateCode")
            return Conflict(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>Updates an assignment group's code, name, and strategy.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(WorkflowAssignmentGroupDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateAssignmentGroupRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out var orgId)) return Forbid();

        var command = new UpdateAssignmentGroupCommand(
            id, orgId, request.Name, request.NameAr, request.AssignmentStrategy,
            request.Code.Trim().ToUpperInvariant());
        var result = await _sender.Send(command, cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.AssignmentGroup.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure && result.Error.Code == "Workflow.AssignmentGroup.DuplicateCode")
            return Conflict(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>Adds a participant to an assignment group.</summary>
    [HttpPost("{id:guid}/members")]
    [ProducesResponseType(typeof(WorkflowGroupMemberDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AddMember(
        Guid id,
        [FromBody] AddGroupMemberRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out var orgId)) return Forbid();

        var command = new AddGroupMemberCommand(
            id, orgId, request.ParticipantId,
            request.CanClaim, request.IsPrimary,
            request.ValidFrom, request.ValidTo);
        var result = await _sender.Send(command, cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.AssignmentGroup.MemberAlreadyExists")
            return Conflict(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>Removes a participant from an assignment group.</summary>
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
            new RemoveGroupMemberCommand(id, orgId, participantId), cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.AssignmentGroup.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure && result.Error.Code == "Workflow.AssignmentGroup.MemberNotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return NoContent();
    }
}

public sealed record CreateAssignmentGroupRequest(
    string Name,
    string? NameAr,
    AssignmentStrategy AssignmentStrategy,
    string? Code = null);

public sealed record UpdateAssignmentGroupRequest(
    string Code,
    string Name,
    string? NameAr,
    AssignmentStrategy AssignmentStrategy);

public sealed record AddGroupMemberRequest(
    Guid ParticipantId,
    DateTime? ValidFrom = null,
    DateTime? ValidTo = null,
    bool CanClaim = true,
    bool IsPrimary = false);
