namespace Workflow.Api.Controllers;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Results;
using Workflow.Application.Commands.CancelWorkflowInstance;
using Workflow.Application.Commands.StartWorkflowInstance;
using Workflow.Application.Commands.SuspendWorkflowInstance;
using Workflow.Application.Commands.ResumeWorkflowInstance;
using Workflow.Application.Commands.RetryFailedActivity;
using Workflow.Application.DTOs;
using Workflow.Application.Queries.GetWorkflowInstanceById;
using Workflow.Application.Queries.GetWorkflowLiveGraph;
using Workflow.Application.Queries.GetWorkflowProgress;
using Workflow.Application.Queries.GetWorkflowTimeline;
using Workflow.Application.Queries.GetWorkflowInstanceByEntity;
using Workflow.Application.Queries.ListWorkflowInstancesByOrg;
using Workflow.Application.Queries.GetWorkflowInstanceForSuperAdmin;
using Workflow.Application.Queries.ListAllWorkflowInstances;
using Workflow.Application.Queries.ListInstanceIncidents;
using Workflow.Application.Queries.ListInstanceTimers;

/// <summary>
/// Organization runtime: start instances, view progress, cancel/suspend/resume (org-scoped).
/// SuperAdmin instance monitor lives on /api/workflow/runtime/admin routes.
/// </summary>
[ApiController]
[Route("api/workflow/runtime")]
[Produces("application/json")]
public sealed class WorkflowRuntimeController : WorkflowControllerBase
{
    private readonly ISender _sender;
    public WorkflowRuntimeController(ISender sender) => _sender = sender;

    private Guid GetOrgId() =>
        Context.OrganizationId;
    private Guid GetUserId() => Context.ActorId;

    /// <summary>Start a new workflow instance for a business entity.</summary>
    [HttpPost("instances")]
    [ProducesResponseType(typeof(WorkflowInstanceDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StartInstance(
        [FromBody] StartWorkflowInstanceRequest body,
        CancellationToken cancellationToken)
    {
        var orgId = GetOrgId();
        if (orgId == Guid.Empty) return Forbid();

        var result = await _sender.Send(new StartWorkflowInstanceCommand(
            orgId, body.WorkflowBindingId, body.BusinessEntityId, body.IdempotencyKey,
            body.CorrelationId, GetUserId()), cancellationToken);

        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return CreatedAtAction(nameof(GetInstance), new { instanceId = result.Value.Id }, result.Value);
    }

    /// <summary>Get a single instance by ID (tenant-scoped).</summary>
    [HttpGet("instances/{instanceId:guid}")]
    [ProducesResponseType(typeof(WorkflowInstanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInstance(Guid instanceId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetWorkflowInstanceByIdQuery(instanceId, GetOrgId()), cancellationToken);
        if (result.IsFailure && result.Error.Code.Contains("NotFound")) return NotFound();
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    /// <summary>Get full progress (instance + activities + events) for a business entity.</summary>
    [HttpGet("instances/{instanceId:guid}/progress")]
    [ProducesResponseType(typeof(WorkflowProgressDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProgress(Guid instanceId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetWorkflowProgressQuery(instanceId, GetOrgId()), cancellationToken);
        if (result.IsFailure && result.Error.Code.Contains("NotFound")) return NotFound();
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    /// <summary>
    /// Designer-shaped live graph for a running or completed instance (tenant-scoped).
    /// Nodes include canvas positions and runtime status for completed / current / pending steps.
    /// </summary>
    [HttpGet("instances/{instanceId:guid}/live-graph")]
    [ProducesResponseType(typeof(WorkflowLiveGraphDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLiveGraph(Guid instanceId, CancellationToken cancellationToken)
    {
        var orgId = GetOrgId();
        if (orgId == Guid.Empty) return Forbid();

        var result = await _sender.Send(
            new GetWorkflowLiveGraphQuery(instanceId, orgId, SuperAdmin: false), cancellationToken);
        if (result.IsFailure && result.Error.Code.Contains("NotFound")) return NotFound();
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    /// <summary>List all workflow instances for the current organization (paginated).</summary>
    [HttpGet("instances")]
    [ProducesResponseType(typeof(PaginatedResult<WorkflowInstanceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListInstances(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new ListWorkflowInstancesByOrgQuery(GetOrgId(), page, pageSize), cancellationToken);
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    /// <summary>Get the ordered event timeline for a workflow instance.</summary>
    [HttpGet("instances/{instanceId:guid}/timeline")]
    [ProducesResponseType(typeof(IReadOnlyList<WorkflowEventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTimeline(Guid instanceId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetWorkflowTimelineQuery(instanceId, GetOrgId()), cancellationToken);
        if (result.IsFailure && result.Error.Code.Contains("NotFound")) return NotFound();
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    /// <summary>Timers attached to a workflow instance (tenant-scoped).</summary>
    [HttpGet("instances/{instanceId:guid}/timers")]
    [ProducesResponseType(typeof(IReadOnlyList<WorkflowTimerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTimers(Guid instanceId, CancellationToken cancellationToken)
    {
        var isSuperAdmin = false;
        var result = await _sender.Send(
            new ListInstanceTimersQuery(instanceId, GetOrgId(), BypassTenantCheck: isSuperAdmin),
            cancellationToken);
        if (result.IsFailure && result.Error.Code.Contains("NotFound")) return NotFound();
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    /// <summary>Safe incident summaries for a workflow instance (no technical details).</summary>
    [HttpGet("instances/{instanceId:guid}/incidents")]
    [ProducesResponseType(typeof(IReadOnlyList<WorkflowIncidentSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetIncidents(Guid instanceId, CancellationToken cancellationToken)
    {
        var isSuperAdmin = false;
        var result = await _sender.Send(
            new ListInstanceIncidentsQuery(instanceId, GetOrgId(), BypassTenantCheck: isSuperAdmin),
            cancellationToken);
        if (result.IsFailure && result.Error.Code.Contains("NotFound")) return NotFound();
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    /// <summary>Find the active workflow instance for a specific business entity.</summary>
    [HttpGet("business/{moduleKey}/{entityType}/{entityId}")]
    [ProducesResponseType(typeof(WorkflowInstanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> GetByBusinessEntity(
        string moduleKey, string entityType, string entityId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetWorkflowInstanceByEntityQuery(GetOrgId(), moduleKey, entityType, entityId),
            cancellationToken);
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        if (result.Value is null) return NoContent();
        return Ok(result.Value);
    }

    /// <summary>Suspend a running workflow instance (OrgAdmin).</summary>
    [HttpPost("instances/{instanceId:guid}/suspend")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Suspend(Guid instanceId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new SuspendWorkflowInstanceCommand(instanceId, GetOrgId(), GetUserId()), cancellationToken);
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return NoContent();
    }

    /// <summary>Resume a suspended workflow instance (OrgAdmin).</summary>
    [HttpPost("instances/{instanceId:guid}/resume")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Resume(Guid instanceId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new ResumeWorkflowInstanceCommand(instanceId, GetOrgId(), GetUserId()), cancellationToken);
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return NoContent();
    }

    /// <summary>Cancel a running or suspended instance.</summary>
    [HttpPost("instances/{instanceId:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(Guid instanceId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new CancelWorkflowInstanceCommand(instanceId, GetOrgId(), GetUserId()), cancellationToken);
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return NoContent();
    }

    // ------------ SuperAdmin cross-tenant runtime monitor ------------

    /// <summary>SuperAdmin: list all instances across all organizations.</summary>
    [HttpGet("admin/instances")]
    [ProducesResponseType(typeof(PaginatedResult<WorkflowInstanceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AdminListInstances(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new ListAllWorkflowInstancesQuery(page, pageSize, organizationId), cancellationToken);
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    /// <summary>SuperAdmin: get full progress for any instance cross-tenant.</summary>
    [HttpGet("admin/instances/{instanceId:guid}")]
    [ProducesResponseType(typeof(WorkflowProgressDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AdminGetInstance(Guid instanceId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetWorkflowInstanceForSuperAdminQuery(instanceId), cancellationToken);
        if (result.IsFailure && result.Error.Code.Contains("NotFound")) return NotFound();
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    /// <summary>
    /// SuperAdmin: designer-shaped live graph for any instance, ignoring tenant filters.
    /// </summary>
    [HttpGet("admin/instances/{instanceId:guid}/live-graph")]
    [ProducesResponseType(typeof(WorkflowLiveGraphDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AdminGetLiveGraph(Guid instanceId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetWorkflowLiveGraphQuery(instanceId, GetOrgId(), SuperAdmin: false), cancellationToken);
        if (result.IsFailure && result.Error.Code.Contains("NotFound")) return NotFound();
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    /// <summary>SuperAdmin: suspend an instance.</summary>
    [HttpPost("admin/instances/{instanceId:guid}/suspend")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AdminSuspend(Guid instanceId, CancellationToken cancellationToken)
    {
        // SuperAdmin operates cross-tenant; pass organizationId=Empty to bypass tenant check
        var result = await _sender.Send(
            new SuspendWorkflowInstanceCommand(instanceId, GetOrgId(), GetUserId()), cancellationToken);
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return NoContent();
    }

    /// <summary>SuperAdmin: resume a suspended instance.</summary>
    [HttpPost("admin/instances/{instanceId:guid}/resume")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AdminResume(Guid instanceId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new ResumeWorkflowInstanceCommand(instanceId, GetOrgId(), GetUserId()), cancellationToken);
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return NoContent();
    }

    /// <summary>SuperAdmin: retry a failed activity.</summary>
    [HttpPost("admin/instances/{instanceId:guid}/retry")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AdminRetry(Guid instanceId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new RetryFailedActivityCommand(instanceId, GetOrgId(), GetUserId()), cancellationToken);
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return NoContent();
    }
}

public sealed record StartWorkflowInstanceRequest(
    Guid WorkflowBindingId,
    string BusinessEntityId,
    string IdempotencyKey,
    string? CorrelationId = null);
