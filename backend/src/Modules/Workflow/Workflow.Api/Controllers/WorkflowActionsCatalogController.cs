namespace Workflow.Api.Controllers;

using NWFM.Shared.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Workflow.Application.DTOs;
using Workflow.Application.Queries.ListWorkflowActionsCatalog;

/// <summary>Catalog of registered service-task / system actions across modules.</summary>
[ApiController]
[Route("api/workflow/actions-catalog")]
[Produces("application/json")]
[Authorize(Policy = NwfmPolicies.ManageDefinitions)]
public sealed class WorkflowActionsCatalogController : WorkflowControllerBase
{
    private readonly ISender _sender;
    public WorkflowActionsCatalogController(ISender sender) => _sender = sender;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<WorkflowActionCatalogEntryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new ListWorkflowActionsCatalogQuery(), cancellationToken);
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }
}
