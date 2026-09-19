namespace Workflow.Api.Controllers;

using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Workflow.Application.Integrations;

[ApiController]
[Route("api/workflow/integrations")]
public sealed class WorkflowIntegrationsController(IWorkflowIntegrations integrations) : WorkflowControllerBase
{
    [HttpGet("connections")]
    public Task<IReadOnlyList<ConnectionDto>> Connections(CancellationToken ct) => integrations.ListConnectionsAsync(ct);
    [HttpPost("connections")]
    public async Task<IActionResult> Create(ConnectionInput input, CancellationToken ct)
    { var result = await integrations.SaveConnectionAsync(null, input, ct); return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error); }
    [HttpPut("connections/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, ConnectionInput input, CancellationToken ct)
    { var result = await integrations.SaveConnectionAsync(id, input, ct); return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error); }
    [HttpDelete("connections/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    { var result = await integrations.DeleteConnectionAsync(id, ct); return result.IsSuccess ? NoContent() : BadRequest(result.Error); }
    [HttpPost("http/test")]
    public async Task<IActionResult> Test(HttpTestRequest request, CancellationToken ct)
    { var result = await integrations.TestHttpAsync(request.Configuration, request.Variables ?? [], ct); return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error); }
    [HttpGet("operations")]
    public Task<IReadOnlyList<OperationDto>> Operations([FromQuery] Guid? instanceId, CancellationToken ct) => integrations.ListOperationsAsync(instanceId, ct);
    [HttpPost("operations/{id:guid}/replay")]
    public async Task<IActionResult> Replay(Guid id, CancellationToken ct)
    { var result = await integrations.ReplayOperationAsync(id, ct); return result.IsSuccess ? NoContent() : BadRequest(result.Error); }
    [HttpPost("webhooks/{connectionId:guid}")]
    [RequestSizeLimit(262144)]
    public async Task<IActionResult> Receive(Guid connectionId, CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        var result = await integrations.ReceiveEventAsync(connectionId, body, Request.Headers["X-Workflow-Timestamp"],
            Request.Headers["X-Workflow-Signature"], Request.Headers["X-Workflow-Key"], ct);
        return result.IsSuccess ? Accepted(new { receiptId = result.Value })
            : result.Error.Code.EndsWith("Unauthorized") ? Unauthorized(result.Error) : BadRequest(result.Error);
    }
    [HttpGet("events")]
    public Task<IReadOnlyList<EventReceiptDto>> Events(CancellationToken ct) => integrations.ListEventsAsync(ct);
    [HttpGet("waits")]
    public Task<IReadOnlyList<EventWaitDto>> Waits(CancellationToken ct) => integrations.ListWaitsAsync(ct);
    [HttpPost("events/{id:guid}/replay")]
    public async Task<IActionResult> ReplayEvent(Guid id, [FromBody] EventReplayRequest request, CancellationToken ct)
    { var result = await integrations.ReplayEventAsync(id, request.ActivityInstanceId, ct); return result.IsSuccess ? NoContent() : BadRequest(result.Error); }
}
public sealed record HttpTestRequest(HttpActivityConfiguration Configuration, Dictionary<string, JsonElement>? Variables);
public sealed record EventReplayRequest(Guid? ActivityInstanceId);
