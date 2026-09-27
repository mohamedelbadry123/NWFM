namespace Workflow.Api.Controllers;

using NWFM.Shared.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Results;
using Workflow.Application.Commands.CloneWorkflowVersion;
using Workflow.Application.Commands.CreateWorkflowDraft;
using Workflow.Application.Commands.PublishWorkflowVersion;
using Workflow.Application.Commands.RetireWorkflowVersion;
using Workflow.Application.Commands.SaveWorkflowDraftXml;
using Workflow.Application.Commands.ValidateWorkflowVersion;
using Workflow.Application.DTOs;
using Workflow.Application.Queries.GetWorkflowPublishPreview;
using Workflow.Application.Queries.GetWorkflowVersionById;
using Workflow.Application.Queries.GetWorkflowVersionProjection;
using Workflow.Application.Queries.GetWorkflowVersionXml;
using Workflow.Application.Queries.ListWorkflowVersions;

/// <summary>Manages workflow versions for organization-owned definitions.</summary>
[ApiController]
[Route("api/workflow/definitions/{definitionId:guid}/versions")]
[Produces("application/json")]
[Authorize(Policy = NwfmPolicies.ManageDefinitions)]
public sealed class WorkflowVersionsController : WorkflowControllerBase
{
    private readonly ISender _sender;

    public WorkflowVersionsController(ISender sender) => _sender = sender;

    private bool TryGetUserId(out Guid userId)
        => TryGetActorId(out userId);

    /// <summary>Returns a paginated list of versions for a workflow definition.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResult<WorkflowVersionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        Guid definitionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new ListWorkflowVersionsQuery(definitionId, page, pageSize), cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Definition.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>Returns a workflow version with its full projection (activities, transitions, variables).</summary>
    [HttpGet("{versionId:guid}")]
    [ProducesResponseType(typeof(WorkflowVersionDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetById(
        Guid definitionId,
        Guid versionId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetWorkflowVersionByIdQuery(versionId), cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Version.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>Returns a publish preview (blocking reasons, warnings, assignment keys, counts).</summary>
    [HttpGet("{versionId:guid}/publish-preview")]
    [ProducesResponseType(typeof(WorkflowPublishPreviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPublishPreview(
        Guid definitionId,
        Guid versionId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetWorkflowPublishPreviewQuery(versionId), cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Version.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>Returns the raw XML content of a workflow version.</summary>
    [HttpGet("{versionId:guid}/xml")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetXml(
        Guid definitionId,
        Guid versionId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetWorkflowVersionXmlQuery(versionId), cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Version.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>Returns the normalized projection (activities, transitions, variables) for a version.</summary>
    [HttpGet("{versionId:guid}/projection")]
    [ProducesResponseType(typeof(WorkflowVersionDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetProjection(
        Guid definitionId,
        Guid versionId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetWorkflowVersionProjectionQuery(versionId), cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Version.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>Creates a new Draft version for the workflow definition.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(WorkflowVersionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateDraft(
        Guid definitionId,
        [FromBody] CreateWorkflowDraftRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        var command = new CreateWorkflowDraftCommand(
            definitionId, userId, request?.ChangeSummary);
        var result = await _sender.Send(command, cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Version.DraftAlreadyExists")
            return Conflict(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure && result.Error.Code == "Workflow.Definition.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>Saves XML content and rebuilds the normalized projection for a Draft version.</summary>
    [HttpPut("{versionId:guid}/xml")]
    [ProducesResponseType(typeof(WorkflowVersionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SaveXml(
        Guid definitionId,
        Guid versionId,
        [FromBody] SaveWorkflowDraftXmlRequest request,
        CancellationToken cancellationToken)
    {
        var command = new SaveWorkflowDraftXmlCommand(
            versionId, request.XmlContent, request.DesignerJson);
        var result = await _sender.Send(command, cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Version.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure && result.Error.Code == "Workflow.Version.NotDraft")
            return UnprocessableEntity(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>Validates a Draft version and stores the validation result.</summary>
    [HttpPost("{versionId:guid}/validate")]
    [ProducesResponseType(typeof(WorkflowValidationResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Validate(
        Guid definitionId,
        Guid versionId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new ValidateWorkflowVersionCommand(versionId), cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Version.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>Publishes a validated Draft version. Immutable once published.</summary>
    [HttpPost("{versionId:guid}/publish")]
    [ProducesResponseType(typeof(WorkflowVersionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Publish(
        Guid definitionId,
        Guid versionId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        var result = await _sender.Send(
            new PublishWorkflowVersionCommand(versionId, userId), cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Version.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>Clones a version into a new Draft for the same definition.</summary>
    [HttpPost("{versionId:guid}/clone")]
    [ProducesResponseType(typeof(WorkflowVersionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Clone(
        Guid definitionId,
        Guid versionId,
        [FromBody] CloneWorkflowVersionRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        var command = new CloneWorkflowVersionCommand(
            versionId, userId, request?.ChangeSummary);
        var result = await _sender.Send(command, cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Version.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure && result.Error.Code == "Workflow.Version.DraftAlreadyExists")
            return Conflict(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>Retires a workflow version. Does not physically delete.</summary>
    [HttpPost("{versionId:guid}/retire")]
    [ProducesResponseType(typeof(WorkflowVersionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Retire(
        Guid definitionId,
        Guid versionId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new RetireWorkflowVersionCommand(versionId), cancellationToken);

        if (result.IsFailure && result.Error.Code == "Workflow.Version.NotFound")
            return NotFound(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure && result.Error.Code == "Workflow.Version.AlreadyRetired")
            return UnprocessableEntity(new { result.Error.Code, result.Error.Message });

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }
}

public sealed record CreateWorkflowDraftRequest(string? ChangeSummary);
public sealed record SaveWorkflowDraftXmlRequest(string XmlContent, string? DesignerJson);
public sealed record CloneWorkflowVersionRequest(string? ChangeSummary);
