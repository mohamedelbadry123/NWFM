namespace Workflow.Api.Controllers;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Results;
using Workflow.Application.Commands.ClaimWorkItem;
using Workflow.Application.Commands.CompleteWorkItem;
using Workflow.Application.Commands.DelegateWorkItem;
using Workflow.Application.Commands.ReassignWorkItem;
using Workflow.Application.Commands.ReleaseWorkItem;
using Workflow.Application.DTOs;
using Workflow.Application.Queries.GetWorkItemById;
using Workflow.Application.Queries.ListAvailableWorkItems;
using Workflow.Application.Queries.ListGroupInboxWorkItems;
using Workflow.Application.Queries.ListMyWorkItems;
using Workflow.Application.Queries.ListOverdueWorkItems;

/// <summary>
/// Organization users: view and act on work items (claim, release, complete).
/// All routes are tenant-scoped via the organizationId application context claim.
/// </summary>
[ApiController]
[Route("api/workflow/work-items")]
[Produces("application/json")]
public sealed class WorkItemsController : WorkflowControllerBase
{
    private readonly ISender _sender;
    public WorkItemsController(ISender sender) => _sender = sender;

    private Guid GetOrgId() =>
        Context.OrganizationId;
    private Guid GetUserId() => Context.ActorId;

    /// <summary>List work items currently claimed by the calling user.</summary>
    [HttpGet("my")]
    [ProducesResponseType(typeof(IReadOnlyList<WorkItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyWorkItems(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new ListMyWorkItemsQuery(GetUserId(), GetOrgId()), cancellationToken);
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    /// <summary>Pending work items assigned to any group the caller belongs to.</summary>
    [HttpGet("available")]
    [ProducesResponseType(typeof(IReadOnlyList<WorkItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailable(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new ListAvailableWorkItemsQuery(GetUserId(), GetOrgId()), cancellationToken);
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    /// <summary>Overdue pending/claimed work items visible to the caller.</summary>
    [HttpGet("overdue")]
    [ProducesResponseType(typeof(IReadOnlyList<WorkItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOverdue(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new ListOverdueWorkItemsQuery(GetUserId(), GetOrgId()), cancellationToken);
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    /// <summary>List pending work items in a specific assignment group.</summary>
    [HttpGet("group/{groupId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<WorkItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGroupInbox(Guid groupId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new ListGroupInboxWorkItemsQuery(groupId, GetOrgId()), cancellationToken);
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    /// <summary>Get a single work item by ID.</summary>
    [HttpGet("{workItemId:guid}")]
    [ProducesResponseType(typeof(WorkItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWorkItem(Guid workItemId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetWorkItemByIdQuery(workItemId, GetOrgId()), cancellationToken);
        if (result.IsFailure && result.Error.Code.Contains("NotFound")) return NotFound();
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    /// <summary>Claim a pending work item. Concurrent claims protected by RowVersion.</summary>
    [HttpPost("{workItemId:guid}/claim")]
    [ProducesResponseType(typeof(WorkItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Claim(Guid workItemId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new ClaimWorkItemCommand(workItemId, GetUserId(), GetOrgId()), cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.WorkItem.ConcurrencyConflict")
            return Conflict(new { result.Error.Code, result.Error.Message });
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    /// <summary>Release a claimed work item back to the group inbox.</summary>
    [HttpPost("{workItemId:guid}/release")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Release(Guid workItemId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new ReleaseWorkItemCommand(workItemId, GetUserId(), GetOrgId()), cancellationToken);
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return NoContent();
    }

    /// <summary>Complete a claimed work item and advance the workflow instance.</summary>
    [HttpPost("{workItemId:guid}/complete")]
    [ProducesResponseType(typeof(WorkItemDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Complete(
        Guid workItemId,
        [FromBody] CompleteWorkItemRequest body,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new CompleteWorkItemCommand(
                workItemId, GetUserId(), GetOrgId(),
                body.ActionTaken, body.Comment,
                body.RedirectAssignmentGroupId, body.RedirectDepartmentId, body.FormValues),
            cancellationToken);
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    /// <summary>Reassign a work item to a different assignment group (releases any claim).</summary>
    [HttpPost("{workItemId:guid}/reassign")]
    [ProducesResponseType(typeof(WorkItemDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reassign(
        Guid workItemId,
        [FromBody] ReassignWorkItemRequest body,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new ReassignWorkItemCommand(workItemId, GetOrgId(), GetUserId(), body.NewAssignmentGroupId),
            cancellationToken);
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    /// <summary>Delegate a claimed work item to another member of the same assignment group.</summary>
    [HttpPost("{workItemId:guid}/delegate")]
    [ProducesResponseType(typeof(WorkItemDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Delegate(
        Guid workItemId,
        [FromBody] DelegateWorkItemRequest body,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new DelegateWorkItemCommand(workItemId, GetOrgId(), GetUserId(), body.DelegateToUserId, body.Comment),
            cancellationToken);
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }
}

public sealed record CompleteWorkItemRequest(
    string ActionTaken,
    string? Comment = null,
    Guid? RedirectAssignmentGroupId = null,
    Guid? RedirectDepartmentId = null,
    Dictionary<string, System.Text.Json.JsonElement>? FormValues = null);
public sealed record ReassignWorkItemRequest(Guid NewAssignmentGroupId);
public sealed record DelegateWorkItemRequest(Guid DelegateToUserId, string? Comment = null);
