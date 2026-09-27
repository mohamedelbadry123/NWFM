namespace Workflow.Api.Controllers;

using NWFM.Shared.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Results;
using Workflow.Application.Commands.ActivateWorkflowDefinition;
using Workflow.Application.Commands.CreateWorkflowDefinition;
using Workflow.Application.Commands.DeactivateWorkflowDefinition;
using Workflow.Application.Commands.UpdateWorkflowDefinition;
using Workflow.Application.DTOs;
using Workflow.Application.Queries.GetWorkflowDefinitionById;
using Workflow.Application.Queries.ListWorkflowDefinitions;

/// <summary>
/// Manages organization-owned workflow definitions.
/// SuperAdmin may pass organizationId. OrgAdmin is scoped to the application context tenant.
/// </summary>
[ApiController]
[Route("api/workflow/definitions")]
[Produces("application/json")]
[Authorize(Policy = NwfmPolicies.ManageDefinitions)]
public sealed class WorkflowDefinitionsController : WorkflowControllerBase
{
    private readonly ISender _sender;

    public WorkflowDefinitionsController(ISender sender) => _sender = sender;

    private bool TryGetContextOrganizationId(out Guid orgId)
        => TryResolveTenant(out orgId) && orgId != Guid.Empty;


    /// <summary>Returns a paginated list of workflow definitions for an organization.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResult<WorkflowDefinitionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] Guid? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveOrganization(organizationId, requireValue: true, out var orgId, out var error))
            return error!;

        var result = await _sender.Send(
            new ListWorkflowDefinitionsQuery(page, pageSize, search, orgId), cancellationToken);

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>Returns a single workflow definition by ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(WorkflowDefinitionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetWorkflowDefinitionByIdQuery(id), cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Definition.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        if (!CallerCanAccess(result.Value.OrganizationId))
            return NotFound(new { Code = "Workflow.Definition.NotFound", Message = "Workflow definition was not found." });

        return Ok(result.Value);
    }

    /// <summary>Creates a new workflow definition owned by an organization.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(WorkflowDefinitionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        [FromBody] CreateWorkflowDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryResolveOrganization(request.OrganizationId, requireValue: true, out var orgId, out var error))
            return error!;

        var command = new CreateWorkflowDefinitionCommand(
            orgId, request.DefinitionKey, request.Name, request.NameAr,
            request.Description, request.DescriptionAr);
        var result = await _sender.Send(command, cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Definition.DuplicateKey")
            return Conflict(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>Updates a workflow definition's name and descriptions.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(WorkflowDefinitionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateWorkflowDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await _sender.Send(new GetWorkflowDefinitionByIdQuery(id), cancellationToken);
        if (existing.IsFailure || !CallerCanAccess(existing.Value.OrganizationId))
            return NotFound(new { Code = "Workflow.Definition.NotFound", Message = "Workflow definition was not found." });

        var command = new UpdateWorkflowDefinitionCommand(
            id, request.Name, request.NameAr, request.Description, request.DescriptionAr);
        var result = await _sender.Send(command, cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Definition.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>Activates a workflow definition.</summary>
    [HttpPost("{id:guid}/activate")]
    [ProducesResponseType(typeof(WorkflowDefinitionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
    {
        var existing = await _sender.Send(new GetWorkflowDefinitionByIdQuery(id), cancellationToken);
        if (existing.IsFailure || !CallerCanAccess(existing.Value.OrganizationId))
            return NotFound(new { Code = "Workflow.Definition.NotFound", Message = "Workflow definition was not found." });

        var result = await _sender.Send(
            new ActivateWorkflowDefinitionCommand(id), cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Definition.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>Deactivates a workflow definition.</summary>
    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(typeof(WorkflowDefinitionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var existing = await _sender.Send(new GetWorkflowDefinitionByIdQuery(id), cancellationToken);
        if (existing.IsFailure || !CallerCanAccess(existing.Value.OrganizationId))
            return NotFound(new { Code = "Workflow.Definition.NotFound", Message = "Workflow definition was not found." });

        var result = await _sender.Send(
            new DeactivateWorkflowDefinitionCommand(id), cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Definition.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    private bool CallerCanAccess(Guid organizationId)
    {
        return TryGetContextOrganizationId(out var jwtOrg) && jwtOrg == organizationId;
    }

    private bool TryResolveOrganization(
        Guid? requested,
        out Guid organizationId,
        out IActionResult? error)
        => TryResolveOrganization(requested, requireValue: false, out organizationId, out error);

    private bool TryResolveOrganization(
        Guid? requested,
        bool requireValue,
        out Guid organizationId,
        out IActionResult? error)
    {
        organizationId = Context.OrganizationId;
        error = null;
        if (requested.HasValue && requested != organizationId) {
            error = BadRequest(new { Code = "Tenant.Mismatch", Message = "The requested tenant is not active." });
            return false;
        }
        return organizationId != Guid.Empty;
    }
}

public sealed record CreateWorkflowDefinitionRequest(
    string DefinitionKey,
    string Name,
    string? NameAr,
    string? Description,
    string? DescriptionAr,
    Guid? OrganizationId = null);

public sealed record UpdateWorkflowDefinitionRequest(
    string Name,
    string? NameAr,
    string? Description,
    string? DescriptionAr);
