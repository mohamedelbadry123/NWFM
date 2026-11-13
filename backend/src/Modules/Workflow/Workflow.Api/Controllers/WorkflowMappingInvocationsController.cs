using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Constants;
using Workflow.Application.Mapping;

namespace Workflow.Api.Controllers;

[ApiController]
[Authorize(Policy = NwfmPolicies.ManageDefinitions)]
[Route("api/workflow/mapping-invocations")]
public sealed class WorkflowMappingInvocationsController(IMappingInvocations invocations) : WorkflowControllerBase
{
    [HttpGet("{id:guid}/prepared-inputs")]
    [ProducesResponseType(typeof(PreparedMappingInputsDto), 200)]
    public async Task<IActionResult> PreparedInputs(Guid id, CancellationToken ct)
    {
        var result = await invocations.ReadPreparedInputsAsync(id, Context.ActorId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<MappingInvocationDto>), 200)]
    public async Task<IActionResult> List([FromQuery] Guid rootInstanceId, CancellationToken ct)
    {
        var result = await invocations.ListAsync(rootInstanceId, Context.ActorId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
    [HttpPost("{id:guid}/retry")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Retry(Guid id, CancellationToken ct)
    {
        var result = await invocations.RetryAsync(id, Context.ActorId, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }
}
