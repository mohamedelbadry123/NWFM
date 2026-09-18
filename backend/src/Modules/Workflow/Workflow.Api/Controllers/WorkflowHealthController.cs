namespace Workflow.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Workflow.Application.Settings;

/// <summary>Workflow module feature-flag probe — safe to call without a application context.</summary>
[ApiController]
[Route("api/workflow")]
[Produces("application/json")]
public sealed class WorkflowHealthController : WorkflowControllerBase
{
    private readonly WorkflowSettings _settings;

    public WorkflowHealthController(IOptions<WorkflowSettings> settings)
        => _settings = settings.Value;

    /// <summary>Returns the module name and whether the feature flag is enabled.</summary>
    [HttpGet("status")]
    public IActionResult GetStatus()
        => Ok(new
        {
            module  = "Workflow",
            enabled = _settings.IsEnabled
        });
}
