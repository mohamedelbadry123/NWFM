namespace Workflow.Api.Controllers;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Results;
using Workflow.Application.Commands.SimulateWorkflowBinding;
using Workflow.Application.DTOs;
using Workflow.Application.Queries.GetWorkflowBindingForSuperAdmin;
using Workflow.Application.Queries.ListActiveGroupsForOrg;
using Workflow.Application.Queries.ListAllWorkflowBindings;

/// <summary>
/// SuperAdmin-only flat catalog of all workflow bindings across all organizations and definitions.
/// Complements WorkflowBindingsController (which is scoped under a definition).
/// Also exposes an org-groups lookup used by the binding wizard mapping step.
/// </summary>
[ApiController]
[Route("api/workflow/bindings")]
[Produces("application/json")]
public sealed class WorkflowBindingsCatalogController : WorkflowControllerBase
{
    private readonly ISender _sender;

    public WorkflowBindingsCatalogController(ISender sender) => _sender = sender;

    /// <summary>
    /// Returns a paginated flat list of all workflow bindings.
    /// Optional filters: definitionId, organizationId.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResult<WorkflowBindingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? definitionId = null,
        [FromQuery] Guid? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new ListAllWorkflowBindingsQuery(page, pageSize, definitionId, organizationId),
            cancellationToken);

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>Returns a single binding by ID, ignoring tenant filter (SuperAdmin cross-tenant).</summary>
    [HttpGet("{bindingId:guid}")]
    [ProducesResponseType(typeof(WorkflowBindingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetById(Guid bindingId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetWorkflowBindingForSuperAdminQuery(bindingId), cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Binding.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>
    /// Dry-run simulation of a binding's resolved published version against an optional sample payload.
    /// Does not persist a workflow instance.
    /// </summary>
    [HttpPost("{bindingId:guid}/simulate")]
    [ProducesResponseType(typeof(WorkflowSimulationResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Simulate(
        Guid bindingId,
        [FromBody] SimulateWorkflowBindingRequest? body,
        CancellationToken cancellationToken)
    {
        body ??= new SimulateWorkflowBindingRequest(null, null);

        var bindingResult = await _sender.Send(
            new GetWorkflowBindingForSuperAdminQuery(bindingId), cancellationToken);
        if (bindingResult.IsFailure && bindingResult.Error.Code == "Workflow.Binding.NotFound")
            return NotFound(new { bindingResult.Error.Code, bindingResult.Error.Message });
        if (bindingResult.IsFailure)
            return BadRequest(new { bindingResult.Error.Code, bindingResult.Error.Message });

        var orgId = body.OrganizationId ?? bindingResult.Value.OrganizationId;

        var result = await _sender.Send(
            new SimulateWorkflowBindingCommand(bindingId, orgId, body.SamplePayloadJson),
            cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Binding.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });
        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>
    /// Returns all active assignment groups for a specific organization.
    /// Used by the SuperAdmin binding wizard to populate assignment-key mapping dropdowns.
    /// </summary>
    [HttpGet("organizations/{organizationId:guid}/groups")]
    [ProducesResponseType(typeof(IReadOnlyList<WorkflowAssignmentGroupDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListOrgGroups(
        Guid organizationId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new ListActiveGroupsForOrgQuery(organizationId), cancellationToken);

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }
}

public sealed record SimulateWorkflowBindingRequest(Guid? OrganizationId, string? SamplePayloadJson);
