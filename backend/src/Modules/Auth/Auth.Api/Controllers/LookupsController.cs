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
    [ProducesResponseType(typeof(Result<PaginatedResult<LookupItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDepartments([FromQuery] GetLookupsQuery query, CancellationToken ct)
    {
        var result = await sender.Send(query with { LookupType = "Department" }, ct);
        return Ok(result);
    }

    [HttpGet("clusters")]
    [ProducesResponseType(typeof(Result<PaginatedResult<LookupItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetClusters([FromQuery] GetLookupsQuery query, CancellationToken ct)
    {
        var result = await sender.Send(query with { LookupType = "Cluster" }, ct);
        return Ok(result);
    }

    [HttpGet("cbus")]
    [ProducesResponseType(typeof(Result<PaginatedResult<LookupItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCbus([FromQuery] GetLookupsQuery query, CancellationToken ct)
    {
        var result = await sender.Send(query with { LookupType = "Cbu" }, ct);
        return Ok(result);
    }

    [HttpGet("branches")]
    [ProducesResponseType(typeof(Result<PaginatedResult<LookupItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBranches([FromQuery] GetLookupsQuery query, CancellationToken ct)
    {
        var result = await sender.Send(query with { LookupType = "Branch" }, ct);
        return Ok(result);
    }

    [HttpGet("operation-areas")]
    [ProducesResponseType(typeof(Result<PaginatedResult<LookupItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOperationAreas([FromQuery] GetLookupsQuery query, CancellationToken ct)
    {
        var result = await sender.Send(query with { LookupType = "OperationArea" }, ct);
        return Ok(result);
    }
}
