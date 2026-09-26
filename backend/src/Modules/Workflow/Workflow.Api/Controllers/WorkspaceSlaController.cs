using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Constants;
using Workflow.Application.Workspace;

namespace Workflow.Api.Controllers;

[ApiController, Route("api/workflow/workspace/sla")]
public sealed class WorkspaceSlaController(IWorkspaceSla sla) : ControllerBase
{
    [HttpGet, Authorize(Policy = NwfmPolicies.ManageSlaPolicies)]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await sla.ListAsync(ct));
    [HttpGet("calendars"), Authorize(Policy = NwfmPolicies.ManageSlaPolicies)]
    public async Task<IActionResult> Calendars(CancellationToken ct) => Ok(await sla.CalendarsAsync(ct));
    [HttpGet("resolve"), Authorize(Policy = NwfmPolicies.ManageDefinitions)]
    public async Task<IActionResult> Resolve(string departmentCode, string fieldActivityCode, CancellationToken ct)
        => Ok(await sla.ResolveAsync(departmentCode, fieldActivityCode, ct));
    [HttpPost, Authorize(Policy = NwfmPolicies.ManageSlaPolicies)]
    public async Task<IActionResult> Create(WorkspaceSlaInput input, CancellationToken ct) => await Save(null, input, ct);
    [HttpPut("{id:guid}"), Authorize(Policy = NwfmPolicies.ManageSlaPolicies)]
    public async Task<IActionResult> Update(Guid id, WorkspaceSlaInput input, CancellationToken ct) => await Save(id, input, ct);
    private async Task<IActionResult> Save(Guid? id, WorkspaceSlaInput input, CancellationToken ct)
    {
        var result = await sla.SaveAsync(id, input, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { result.Error.Code, result.Error.Message });
    }
}
