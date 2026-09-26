namespace Workflow.Api.Controllers;

using NWFM.Shared.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Workflow.Application.DTOs;
using Workflow.Application.Queries.GetWorkflowRequestById;
using Workflow.Application.Queries.ListWorkflowRequests;
using Workflow.Domain.Enums;

/// <summary>
/// Organization request list/read model (MD §14). Tenant-scoped via application context organizationId.
/// </summary>
[ApiController]
[Route("api/workflow/requests")]
[Produces("application/json")]
[Authorize(Policy = NwfmPolicies.ViewInstances)]
public sealed class WorkflowRequestsController : WorkflowControllerBase
{
    private readonly ISender _sender;
    public WorkflowRequestsController(ISender sender) => _sender = sender;

    private Guid GetOrgId() =>
        Context.OrganizationId;

    /// <summary>Paged workflow requests for the current organization.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(WorkflowRequestPageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] WorkflowInstanceStatus? status = null,
        [FromQuery] string? service = null,
        [FromQuery] string? currentStep = null,
        [FromQuery] Guid? originalGroupId = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] string? slaStatus = null,
        [FromQuery] string? sortBy = null,
        CancellationToken cancellationToken = default)
    {
        var orgId = GetOrgId();
        if (orgId == Guid.Empty) return Forbid();

        var result = await _sender.Send(
            new ListWorkflowRequestsQuery(
                orgId, page, pageSize, search, status, service, currentStep, originalGroupId,
                fromUtc, toUtc, slaStatus, sortBy),
            cancellationToken);
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    /// <summary>Get a workflow request by request id.</summary>
    [HttpGet("{requestId:guid}")]
    [ProducesResponseType(typeof(WorkflowRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid requestId, CancellationToken cancellationToken)
    {
        var orgId = GetOrgId();
        if (orgId == Guid.Empty) return Forbid();

        var result = await _sender.Send(
            new GetWorkflowRequestByIdQuery(requestId, orgId), cancellationToken);
        if (result.IsFailure && result.Error.Code.Contains("NotFound")) return NotFound();
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    /// <summary>Get a workflow request by runtime instance id.</summary>
    [HttpGet("by-instance/{instanceId:guid}")]
    [ProducesResponseType(typeof(WorkflowRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByInstance(Guid instanceId, CancellationToken cancellationToken)
    {
        var orgId = GetOrgId();
        if (orgId == Guid.Empty) return Forbid();

        var result = await _sender.Send(
            new GetWorkflowRequestByInstanceIdQuery(instanceId, orgId), cancellationToken);
        if (result.IsFailure && result.Error.Code.Contains("NotFound")) return NotFound();
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }
}
