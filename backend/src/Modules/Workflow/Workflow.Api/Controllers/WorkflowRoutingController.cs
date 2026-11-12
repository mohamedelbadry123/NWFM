using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using Workflow.Application.Routing;

namespace Workflow.Api.Controllers;

[ApiController]
[Authorize(Policy = NwfmPolicies.ManageDefinitions)]
[Route("api/workflow/definitions/{definitionId:guid}/versions/{versionId:guid}/routing")]
public sealed class WorkflowRoutingController(IWorkflowRouting routing, MediatR.ISender sender) : ControllerBase
{
    private IActionResult Respond<T>(Result<T> result) => result.IsSuccess ? new JsonResult(result.Value)
        : result.Error.Code.EndsWith("NotFound", StringComparison.Ordinal) ? NotFound(result.Error)
        : result.Error.Code.EndsWith("Conflict", StringComparison.Ordinal) ? Conflict(result.Error) : BadRequest(result.Error);
    [HttpPost("prepare")]
    [ProducesResponseType(typeof(string), 200)]
    public async Task<IActionResult> Prepare(Guid definitionId, Guid versionId, RoutingUpgradeInput input, CancellationToken ct) => Respond(await routing.PrepareAsync(definitionId, versionId, input, ct));
    [HttpPost("validate-design")]
    [ProducesResponseType(typeof(WorkflowDesignValidation), 200)]
    public async Task<IActionResult> ValidateDesign(Guid definitionId, Guid versionId, DesignValidationInput input, CancellationToken ct) => Respond(await sender.Send(new ValidateWorkflowDesign(definitionId, versionId, input.XmlContent, input.DesignerJson, input.EditId), ct));
    public sealed record DesignValidationInput(string XmlContent, string? DesignerJson, long EditId);
    [HttpGet("context")]
    [ProducesResponseType(typeof(RoutingContext), 200)]
    public async Task<IActionResult> Context(Guid definitionId, Guid versionId, CancellationToken ct) => Respond(await routing.ContextAsync(definitionId, versionId, ct));
    [HttpPost("simulate")]
    [ProducesResponseType(typeof(RoutingSimulationResult), 200)]
    public async Task<IActionResult> Simulate(Guid definitionId, Guid versionId, RoutingSimulationInput input, CancellationToken ct) => Respond(await routing.SimulateAsync(definitionId, versionId, input, ct));
    [HttpGet("upgrade-preview")]
    [ProducesResponseType(typeof(RoutingUpgradePreview), 200)]
    public async Task<IActionResult> Preview(Guid definitionId, Guid versionId, CancellationToken ct) => Respond(await routing.PreviewUpgradeAsync(definitionId, versionId, ct));
    [HttpPost("upgrade")]
    [ProducesResponseType(typeof(string), 200)]
    public async Task<IActionResult> Upgrade(Guid definitionId, Guid versionId, RoutingUpgradeInput input, CancellationToken ct) => Respond(await routing.UpgradeAsync(definitionId, versionId, input, ct));
}
