using Auth.Api.Contracts;
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
[Authorize]
[Route("api/v1/lookups")]
public sealed class LookupsController(ISender sender) : ControllerBase
{
    [HttpGet("field-activity-types")]
    public Task<IActionResult> GetFieldActivityTypes([FromQuery] LookupListQuery query, CancellationToken ct) => Get("FieldActivityType", query, ct);

    [HttpPost("field-activity-types")]
    [Authorize(Policy = NwfmPolicies.ManageLookups)]
    public Task<IActionResult> CreateFieldActivityType(CreateLookupRequest request, CancellationToken ct) => Create("FieldActivityType", request, ct);

    [HttpPut("field-activity-types/{id:guid}")]
    [Authorize(Policy = NwfmPolicies.ManageLookups)]
    public Task<IActionResult> UpdateFieldActivityType(Guid id, UpdateLookupRequest request, CancellationToken ct) => Update("FieldActivityType", id, request, ct);

    [HttpPut("field-activity-types/{id:guid}/status")]
    [Authorize(Policy = NwfmPolicies.ManageLookups)]
    public Task<IActionResult> SetFieldActivityTypeStatus(Guid id, SetLookupStatusRequest request, CancellationToken ct) => SetStatus("FieldActivityType", id, request, ct);

    [HttpGet("departments")]
    public Task<IActionResult> GetDepartments([FromQuery] LookupListQuery query, CancellationToken ct) =>
        Get("Department", query, ct);

    [HttpPost("departments")]
    [Authorize(Policy = NwfmPolicies.ManageLookups)]
    public Task<IActionResult> CreateDepartment([FromBody] CreateLookupRequest request, CancellationToken ct) =>
        Create("Department", request, ct);

    [HttpPut("departments/{id:guid}")]
    [Authorize(Policy = NwfmPolicies.ManageLookups)]
    public Task<IActionResult> UpdateDepartment(Guid id, [FromBody] UpdateLookupRequest request, CancellationToken ct) =>
        Update("Department", id, request, ct);

    [HttpPut("departments/{id:guid}/status")]
    [Authorize(Policy = NwfmPolicies.ManageLookups)]
    public Task<IActionResult> SetDepartmentStatus(Guid id, [FromBody] SetLookupStatusRequest request, CancellationToken ct) =>
        SetStatus("Department", id, request, ct);

    [HttpGet("clusters")]
    public Task<IActionResult> GetClusters([FromQuery] LookupListQuery query, CancellationToken ct) =>
        Get("Cluster", query, ct);

    [HttpPost("clusters")]
    [Authorize(Policy = NwfmPolicies.ManageLookups)]
    public Task<IActionResult> CreateCluster([FromBody] CreateLookupRequest request, CancellationToken ct) =>
        Create("Cluster", request, ct);

    [HttpPut("clusters/{id:guid}")]
    [Authorize(Policy = NwfmPolicies.ManageLookups)]
    public Task<IActionResult> UpdateCluster(Guid id, [FromBody] UpdateLookupRequest request, CancellationToken ct) =>
        Update("Cluster", id, request, ct);

    [HttpPut("clusters/{id:guid}/status")]
    [Authorize(Policy = NwfmPolicies.ManageLookups)]
    public Task<IActionResult> SetClusterStatus(Guid id, [FromBody] SetLookupStatusRequest request, CancellationToken ct) =>
        SetStatus("Cluster", id, request, ct);

    [HttpGet("cbus")]
    public Task<IActionResult> GetCbus([FromQuery] LookupListQuery query, CancellationToken ct) =>
        Get("Cbu", query, ct);

    [HttpPost("cbus")]
    [Authorize(Policy = NwfmPolicies.ManageLookups)]
    public Task<IActionResult> CreateCbu([FromBody] CreateLookupRequest request, CancellationToken ct) =>
        Create("Cbu", request, ct);

    [HttpPut("cbus/{id:guid}")]
    [Authorize(Policy = NwfmPolicies.ManageLookups)]
    public Task<IActionResult> UpdateCbu(Guid id, [FromBody] UpdateLookupRequest request, CancellationToken ct) =>
        Update("Cbu", id, request, ct);

    [HttpPut("cbus/{id:guid}/status")]
    [Authorize(Policy = NwfmPolicies.ManageLookups)]
    public Task<IActionResult> SetCbuStatus(Guid id, [FromBody] SetLookupStatusRequest request, CancellationToken ct) =>
        SetStatus("Cbu", id, request, ct);

    [HttpGet("branches")]
    public Task<IActionResult> GetBranches([FromQuery] LookupListQuery query, CancellationToken ct) =>
        Get("Branch", query, ct);

    [HttpPost("branches")]
    [Authorize(Policy = NwfmPolicies.ManageLookups)]
    public Task<IActionResult> CreateBranch([FromBody] CreateLookupRequest request, CancellationToken ct) =>
        Create("Branch", request, ct);

    [HttpPut("branches/{id:guid}")]
    [Authorize(Policy = NwfmPolicies.ManageLookups)]
    public Task<IActionResult> UpdateBranch(Guid id, [FromBody] UpdateLookupRequest request, CancellationToken ct) =>
        Update("Branch", id, request, ct);

    [HttpPut("branches/{id:guid}/status")]
    [Authorize(Policy = NwfmPolicies.ManageLookups)]
    public Task<IActionResult> SetBranchStatus(Guid id, [FromBody] SetLookupStatusRequest request, CancellationToken ct) =>
        SetStatus("Branch", id, request, ct);

    [HttpGet("operation-areas")]
    public Task<IActionResult> GetOperationAreas([FromQuery] LookupListQuery query, CancellationToken ct) =>
        Get("OperationArea", query, ct);

    [HttpPost("operation-areas")]
    [Authorize(Policy = NwfmPolicies.ManageLookups)]
    public Task<IActionResult> CreateOperationArea([FromBody] CreateLookupRequest request, CancellationToken ct) =>
        Create("OperationArea", request, ct);

    [HttpPut("operation-areas/{id:guid}")]
    [Authorize(Policy = NwfmPolicies.ManageLookups)]
    public Task<IActionResult> UpdateOperationArea(Guid id, [FromBody] UpdateLookupRequest request, CancellationToken ct) =>
        Update("OperationArea", id, request, ct);

    [HttpPut("operation-areas/{id:guid}/status")]
    [Authorize(Policy = NwfmPolicies.ManageLookups)]
    public Task<IActionResult> SetOperationAreaStatus(Guid id, [FromBody] SetLookupStatusRequest request, CancellationToken ct) =>
        SetStatus("OperationArea", id, request, ct);

    private async Task<IActionResult> Get(string type, LookupListQuery query, CancellationToken ct)
    {
        var result = await sender.Send(new GetLookupsQuery
        {
            LookupType = type,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize,
            SearchTerm = query.SearchTerm,
            ParentCode = query.ParentCode,
            IsActive = query.IsActive
        }, ct);
        return Ok(result);
    }

    private async Task<IActionResult> Create(string type, CreateLookupRequest request, CancellationToken ct)
    {
        var result = await sender.Send(new CreateLookupCommand
        {
            LookupType = type,
            Code = request.Code,
            NameEn = request.NameEn,
            NameAr = request.NameAr,
            ParentCode = request.ParentCode
        }, ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result) : BadRequest(result);
    }

    private async Task<IActionResult> Update(string type, Guid id, UpdateLookupRequest request, CancellationToken ct)
    {
        var result = await sender.Send(new UpdateLookupCommand
        {
            LookupType = type,
            Id = id,
            NameEn = request.NameEn,
            NameAr = request.NameAr,
            ParentCode = request.ParentCode
        }, ct);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    private async Task<IActionResult> SetStatus(string type, Guid id, SetLookupStatusRequest request, CancellationToken ct)
    {
        var result = await sender.Send(new SetLookupStatusCommand
        {
            LookupType = type,
            Id = id,
            IsActive = request.IsActive
        }, ct);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }
}
