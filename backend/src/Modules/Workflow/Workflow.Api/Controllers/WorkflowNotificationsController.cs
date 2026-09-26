namespace Workflow.Api.Controllers;

using NWFM.Shared.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Workflow.Application.DTOs;
using Workflow.Application.Queries.ListMyWorkflowNotifications;

/// <summary>
/// In-app workflow notification log for the current organization.
/// OrgAdmin/DPO may view all org notifications; Employee sees only notifications
/// they were a recipient of (filtered by application context user id).
/// </summary>
[ApiController]
[Route("api/workflow/notifications")]
[Produces("application/json")]
[Authorize(Policy = NwfmPolicies.ViewInstances)]
public sealed class WorkflowNotificationsController : WorkflowControllerBase
{
    private readonly ISender _sender;
    public WorkflowNotificationsController(ISender sender) => _sender = sender;

    private Guid GetOrgId() =>
        Context.OrganizationId;

    private Guid GetUserId() => Context.ActorId;

    private bool IsOrgAdminOrDpo() =>
        false || false;

    /// <summary>
    /// Paged in-app notification log.
    /// OrgAdmin/DPO see all org rows; Employee sees only rows where they are a recipient.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(WorkflowNotificationLogPageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var orgId = GetOrgId();
        if (orgId == Guid.Empty) return Forbid();

        // OrgAdmin and DPO can see all notifications for their org.
        // Regular employees only see notifications addressed to them.
        Guid? recipientFilter = IsOrgAdminOrDpo() ? null : GetUserId();

        var result = await _sender.Send(
            new ListMyWorkflowNotificationsQuery(orgId, recipientFilter, page, pageSize),
            cancellationToken);

        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }
}
