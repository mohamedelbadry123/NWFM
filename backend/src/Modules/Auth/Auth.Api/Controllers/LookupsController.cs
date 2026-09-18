using Auth.Application.Lookups.Commands.CreateLookup;
using Auth.Application.Lookups.Commands.SetLookupStatus;
using Auth.Application.Lookups.Commands.UpdateLookup;
using Auth.Application.Lookups.Queries;
using NWFM.Shared.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Results;

namespace Auth.Api.Controllers;

[ApiController]
[Authorize(Policy = NwfmPolicies.ManageLookups)]
[Route("api/v1/lookups")]
public sealed class LookupsController(ISender sender) : ControllerBase
{
    [HttpGet("departments")]
    public Task<IActionResult> GetDepartments([FromQuery] GetLookupsQuery query, CancellationToken ct) =>
        Get("Department", query, ct);

    [HttpPost("departments")]
    public Task<IActionResult> CreateDepartment([FromBody] CreateLookupCommand command, CancellationToken ct) =>
        Create("Department", command, ct);

    [HttpPut("departments/{id:guid}")]
    public Task<IActionResult> UpdateDepartment(Guid id, [FromBody] UpdateLookupCommand command, CancellationToken ct) =>
        Update("Department", id, command, ct);

    [HttpPut("departments/{id:guid}/status")]
    public Task<IActionResult> SetDepartmentStatus(Guid id, [FromBody] SetLookupStatusCommand command, CancellationToken ct) =>
        SetStatus("Department", id, command, ct);

    [HttpGet("clusters")]
    public Task<IActionResult> GetClusters([FromQuery] GetLookupsQuery query, CancellationToken ct) =>
        Get("Cluster", query, ct);

    [HttpPost("clusters")]
    public Task<IActionResult> CreateCluster([FromBody] CreateLookupCommand command, CancellationToken ct) =>
        Create("Cluster", command, ct);

    [HttpPut("clusters/{id:guid}")]
    public Task<IActionResult> UpdateCluster(Guid id, [FromBody] UpdateLookupCommand command, CancellationToken ct) =>
        Update("Cluster", id, command, ct);

    [HttpPut("clusters/{id:guid}/status")]
    public Task<IActionResult> SetClusterStatus(Guid id, [FromBody] SetLookupStatusCommand command, CancellationToken ct) =>
        SetStatus("Cluster", id, command, ct);

    [HttpGet("cbus")]
    public Task<IActionResult> GetCbus([FromQuery] GetLookupsQuery query, CancellationToken ct) =>
        Get("Cbu", query, ct);

    [HttpPost("cbus")]
    public Task<IActionResult> CreateCbu([FromBody] CreateLookupCommand command, CancellationToken ct) =>
        Create("Cbu", command, ct);

    [HttpPut("cbus/{id:guid}")]
    public Task<IActionResult> UpdateCbu(Guid id, [FromBody] UpdateLookupCommand command, CancellationToken ct) =>
        Update("Cbu", id, command, ct);

    [HttpPut("cbus/{id:guid}/status")]
    public Task<IActionResult> SetCbuStatus(Guid id, [FromBody] SetLookupStatusCommand command, CancellationToken ct) =>
        SetStatus("Cbu", id, command, ct);

    [HttpGet("branches")]
    public Task<IActionResult> GetBranches([FromQuery] GetLookupsQuery query, CancellationToken ct) =>
        Get("Branch", query, ct);

    [HttpPost("branches")]
    public Task<IActionResult> CreateBranch([FromBody] CreateLookupCommand command, CancellationToken ct) =>
        Create("Branch", command, ct);

    [HttpPut("branches/{id:guid}")]
    public Task<IActionResult> UpdateBranch(Guid id, [FromBody] UpdateLookupCommand command, CancellationToken ct) =>
        Update("Branch", id, command, ct);

    [HttpPut("branches/{id:guid}/status")]
    public Task<IActionResult> SetBranchStatus(Guid id, [FromBody] SetLookupStatusCommand command, CancellationToken ct) =>
        SetStatus("Branch", id, command, ct);

    [HttpGet("operation-areas")]
    public Task<IActionResult> GetOperationAreas([FromQuery] GetLookupsQuery query, CancellationToken ct) =>
        Get("OperationArea", query, ct);

    [HttpPost("operation-areas")]
    public Task<IActionResult> CreateOperationArea([FromBody] CreateLookupCommand command, CancellationToken ct) =>
        Create("OperationArea", command, ct);

    [HttpPut("operation-areas/{id:guid}")]
    public Task<IActionResult> UpdateOperationArea(Guid id, [FromBody] UpdateLookupCommand command, CancellationToken ct) =>
        Update("OperationArea", id, command, ct);

    [HttpPut("operation-areas/{id:guid}/status")]
    public Task<IActionResult> SetOperationAreaStatus(Guid id, [FromBody] SetLookupStatusCommand command, CancellationToken ct) =>
        SetStatus("OperationArea", id, command, ct);

    private async Task<IActionResult> Get(string type, GetLookupsQuery query, CancellationToken ct)
    {
        var result = await sender.Send(query with { LookupType = type }, ct);
        return Ok(result);
    }

    private async Task<IActionResult> Create(string type, CreateLookupCommand command, CancellationToken ct)
    {
        var result = await sender.Send(command with { LookupType = type }, ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result) : BadRequest(result);
    }

    private async Task<IActionResult> Update(string type, Guid id, UpdateLookupCommand command, CancellationToken ct)
    {
        var result = await sender.Send(command with { LookupType = type, Id = id }, ct);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    private async Task<IActionResult> SetStatus(string type, Guid id, SetLookupStatusCommand command, CancellationToken ct)
    {
        var result = await sender.Send(command with { LookupType = type, Id = id }, ct);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }
}
