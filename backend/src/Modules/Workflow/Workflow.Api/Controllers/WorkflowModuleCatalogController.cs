namespace Workflow.Api.Controllers;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Workflow.Application.Constants;
using Workflow.Application.Queries.GetModuleCatalog;

/// <summary>
/// Exposes the static Module Capability Catalog used by the SuperAdmin binding wizard.
/// Returns all modules, entity types, and trigger events with bilingual labels.
/// SuperAdmin only — no free-text module/event names need to be typed.
/// </summary>
[ApiController]
[Route("api/workflow/module-catalog")]
[Produces("application/json")]
public sealed class WorkflowModuleCatalogController : WorkflowControllerBase
{
    private readonly ISender _sender;

    public WorkflowModuleCatalogController(ISender sender) => _sender = sender;

    /// <summary>
    /// Returns all bindable modules, their entity types, and available trigger events with bilingual labels.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ModuleCatalogEntry>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCatalog(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetModuleCatalogQuery(), cancellationToken);

        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }
}
