namespace Workflow.Api.Controllers;

using NWFM.Shared.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Workflow.Application.Commands.ReplayInboxMessage;
using Workflow.Application.Commands.ReplayOutboxMessage;
using Workflow.Application.DTOs;
using Workflow.Application.Queries.ListDeadLetterInbox;
using Workflow.Application.Queries.ListFailedOutbox;
using NWFM.Shared.Results;

/// <summary>
/// Dead-letter inbox and failed outbox inspection / replay for the workflow integration pipes.
/// </summary>
[ApiController]
[Route("api/workflow/integration-messages")]
[Produces("application/json")]
[Authorize(Policy = NwfmPolicies.ViewInstances)]
public sealed class WorkflowIntegrationMessagesController : WorkflowControllerBase
{
    private readonly ISender _sender;

    public WorkflowIntegrationMessagesController(ISender sender) => _sender = sender;

    private Guid GetOrgId() =>
        Context.OrganizationId;

    /// <summary>Lists dead-lettered inbox messages for the current organization.</summary>
    [HttpGet("inbox/dead-letters")]
    [ProducesResponseType(typeof(PaginatedResult<WorkflowIntegrationInboxMessageDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListDeadLetters(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new ListDeadLetterInboxQuery(GetOrgId(), page, pageSize), cancellationToken);
        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    /// <summary>Replays a dead-letter inbox message (DeadLetter/Failed → Pending, AttemptCount=0).</summary>
    [HttpPost("inbox/{messageId:guid}/replay")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReplayInbox(Guid messageId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new ReplayInboxMessageCommand(messageId, GetOrgId()), cancellationToken);
        if (result.IsFailure && result.Error.Code == "Workflow.Inbox.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });
        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });
        return NoContent();
    }

    /// <summary>Lists failed outbox messages for the current organization.</summary>
    [HttpGet("outbox/failed")]
    [ProducesResponseType(typeof(PaginatedResult<WorkflowIntegrationOutboxMessageDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListFailedOutbox(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new ListFailedOutboxQuery(GetOrgId(), page, pageSize), cancellationToken);
        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    /// <summary>Replays a failed outbox message (Failed → Pending, AttemptCount=0).</summary>
    [HttpPost("outbox/{messageId:guid}/replay")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReplayOutbox(Guid messageId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new ReplayOutboxMessageCommand(messageId, GetOrgId()), cancellationToken);
        if (result.IsFailure && result.Error.Code == "Workflow.Outbox.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });
        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });
        return NoContent();
    }
}
