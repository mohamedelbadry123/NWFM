namespace Workflow.Api.Controllers;

using NWFM.Shared.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Results;
using Workflow.Application.Commands.ActivateWorkflowBinding;
using Workflow.Application.Commands.CreateWorkflowBinding;
using Workflow.Application.Commands.CreateWorkflowBindingAssignmentMapping;
using Workflow.Application.Commands.DeactivateWorkflowBinding;
using Workflow.Application.Commands.DeleteWorkflowBindingAssignmentMapping;
using Workflow.Application.Commands.UpdateWorkflowBinding;
using Workflow.Application.Commands.UpdateWorkflowBindingAssignmentMapping;
using Workflow.Application.DTOs;
using Workflow.Application.Queries.GetWorkflowBindingById;
using Workflow.Application.Queries.GetWorkflowBindingReadiness;
using Workflow.Application.Queries.ListWorkflowBindingAssignmentMappings;
using Workflow.Application.Queries.ListWorkflowBindingsByDefinition;
using Workflow.Domain.Enums;

/// <summary>
/// Manages workflow bindings (definition → org module/entity/trigger mapping).
/// SuperAdmin only — organization users receive HTTP 403.
/// Bindings are design-time configuration only; they do not start workflow instances.
/// </summary>
[ApiController]
[Route("api/workflow/definitions/{definitionId:guid}/bindings")]
[Produces("application/json")]
[Authorize(Policy = NwfmPolicies.ManageBindings)]
public sealed class WorkflowBindingsController : WorkflowControllerBase
{
    private readonly ISender _sender;

    public WorkflowBindingsController(ISender sender) => _sender = sender;

    /// <summary>Returns a paginated list of bindings for a specific workflow definition.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResult<WorkflowBindingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        Guid definitionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new ListWorkflowBindingsByDefinitionQuery(definitionId, page, pageSize), cancellationToken);

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>Returns a single workflow binding by ID.</summary>
    [HttpGet("{bindingId:guid}")]
    [ProducesResponseType(typeof(WorkflowBindingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetById(
        Guid definitionId, Guid bindingId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetWorkflowBindingByIdQuery(bindingId), cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Binding.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>
    /// Returns whether the binding is ready for Shadow/Active use (published version + mapped assignment keys).
    /// </summary>
    [HttpGet("{bindingId:guid}/readiness")]
    [ProducesResponseType(typeof(WorkflowBindingReadinessDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetReadiness(
        Guid definitionId, Guid bindingId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetWorkflowBindingReadinessQuery(bindingId), cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Binding.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>Creates a new binding linking a definition to an organization's module trigger.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(WorkflowBindingDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        Guid definitionId,
        [FromBody] CreateWorkflowBindingRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CreateWorkflowBindingCommand(
            definitionId, request.OrganizationId,
            request.ModuleKey, request.EntityType, request.TriggerEvent,
            request.Description,
            request.Mode,
            request.VersionPolicy,
            request.FixedWorkflowVersionId,
            request.StartEventKey,
            request.StartConditionExpression,
            request.ScreenKey,
            request.InputMappingJson,
            request.OutcomeMappingJson,
            request.ConditionJson,
            request.ExecutionPolicy);
        var result = await _sender.Send(command, cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Binding.DuplicateBinding")
            return Conflict(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure && result.Error.Code == "Workflow.Definition.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>Updates a binding's configuration including mode, version policy, and trigger settings.</summary>
    [HttpPut("{bindingId:guid}")]
    [ProducesResponseType(typeof(WorkflowBindingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(
        Guid definitionId, Guid bindingId,
        [FromBody] UpdateWorkflowBindingRequest request,
        CancellationToken cancellationToken)
    {
        var command = new UpdateWorkflowBindingCommand(
            bindingId, request.ModuleKey, request.EntityType,
            request.TriggerEvent, request.Description,
            request.Mode, request.VersionPolicy,
            request.FixedWorkflowVersionId,
            request.StartEventKey,
            request.StartConditionExpression,
            request.ScreenKey,
            request.InputMappingJson,
            request.OutcomeMappingJson,
            request.ConditionJson,
            request.ExecutionPolicy);
        var result = await _sender.Send(command, cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Binding.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>Deactivates a binding (sets IsActive=false and Mode=Disabled).</summary>
    [HttpPost("{bindingId:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Deactivate(
        Guid definitionId, Guid bindingId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new DeactivateWorkflowBindingCommand(bindingId), cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Binding.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return NoContent();
    }

    /// <summary>
    /// Activates a binding. Shadow/Active mode requires all AssignmentKeys to be mapped
    /// to organization groups. Disabled mode activates unconditionally.
    /// </summary>
    [HttpPost("{bindingId:guid}/activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Activate(
        Guid definitionId, Guid bindingId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new ActivateWorkflowBindingCommand(bindingId), cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Binding.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return NoContent();
    }

    // ── Assignment Mappings ─────────────────────────────────────────────────

    /// <summary>Lists all assignment key→group mappings for a binding in a specific organization.</summary>
    [HttpGet("{bindingId:guid}/organizations/{organizationId:guid}/mappings")]
    [ProducesResponseType(typeof(List<WorkflowBindingAssignmentMappingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListMappings(
        Guid definitionId, Guid bindingId, Guid organizationId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new ListWorkflowBindingAssignmentMappingsQuery(bindingId, organizationId), cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Binding.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>Creates a new assignment mapping (AssignmentKey → AssignmentGroup) for a binding.</summary>
    [HttpPost("{bindingId:guid}/organizations/{organizationId:guid}/mappings")]
    [ProducesResponseType(typeof(WorkflowBindingAssignmentMappingDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateMapping(
        Guid definitionId, Guid bindingId, Guid organizationId,
        [FromBody] CreateWorkflowBindingAssignmentMappingRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new CreateWorkflowBindingAssignmentMappingCommand(
                bindingId, organizationId, request.AssignmentKey, request.AssignmentGroupId),
            cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.AssignmentMapping.DuplicateKey")
            return Conflict(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>Updates the AssignmentGroup for an existing mapping.</summary>
    [HttpPut("{bindingId:guid}/organizations/{organizationId:guid}/mappings/{mappingId:guid}")]
    [ProducesResponseType(typeof(WorkflowBindingAssignmentMappingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateMapping(
        Guid definitionId, Guid bindingId, Guid organizationId, Guid mappingId,
        [FromBody] UpdateWorkflowBindingAssignmentMappingRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new UpdateWorkflowBindingAssignmentMappingCommand(mappingId, organizationId, request.AssignmentGroupId),
            cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.AssignmentMapping.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>Deactivates (soft-deletes) an assignment mapping.</summary>
    [HttpDelete("{bindingId:guid}/organizations/{organizationId:guid}/mappings/{mappingId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteMapping(
        Guid definitionId, Guid bindingId, Guid organizationId, Guid mappingId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new DeleteWorkflowBindingAssignmentMappingCommand(mappingId, organizationId),
            cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.AssignmentMapping.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return NoContent();
    }
}

public sealed record CreateWorkflowBindingRequest(
    Guid OrganizationId,
    string ModuleKey,
    string EntityType,
    string TriggerEvent,
    string? Description,
    WorkflowBindingMode Mode,
    WorkflowVersionPolicy VersionPolicy,
    Guid? FixedWorkflowVersionId,
    string? StartEventKey,
    string? StartConditionExpression,
    string? ScreenKey = null,
    string? InputMappingJson = null,
    string? OutcomeMappingJson = null,
    string? ConditionJson = null,
    WorkflowExecutionPolicy ExecutionPolicy = WorkflowExecutionPolicy.StartNewInstance);

public sealed record UpdateWorkflowBindingRequest(
    string ModuleKey,
    string EntityType,
    string TriggerEvent,
    string? Description,
    WorkflowBindingMode Mode,
    WorkflowVersionPolicy VersionPolicy,
    Guid? FixedWorkflowVersionId,
    string? StartEventKey,
    string? StartConditionExpression,
    string? ScreenKey = null,
    string? InputMappingJson = null,
    string? OutcomeMappingJson = null,
    string? ConditionJson = null,
    WorkflowExecutionPolicy? ExecutionPolicy = null);

public sealed record CreateWorkflowBindingAssignmentMappingRequest(
    string AssignmentKey,
    Guid AssignmentGroupId);

public sealed record UpdateWorkflowBindingAssignmentMappingRequest(
    Guid AssignmentGroupId);
