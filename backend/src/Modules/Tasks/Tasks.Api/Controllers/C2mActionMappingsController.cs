using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using Tasks.Api.Common;
using Tasks.Application.C2m;

namespace Tasks.Api.Controllers;

/// <summary>
/// What each <c>Action Taken</c> answer tells C2M when a task closes its field activity — the lookup
/// behind the form options that do not name a status themselves.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/c2m-action-mappings")]
public sealed class C2mActionMappingsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = NwfmPolicies.ManageTaskTypes)]
    [ProducesResponseType(typeof(Result<PaginatedResult<C2mActionMappingDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] GetC2mActionMappingsQuery query, CancellationToken ct) =>
        (await sender.Send(query, ct)).ToActionResult();

    [HttpPost]
    [Authorize(Policy = NwfmPolicies.ManageTaskTypes)]
    [ProducesResponseType(typeof(Result<Guid>), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateC2mActionMappingCommand command, CancellationToken ct) =>
        (await sender.Send(command, ct)).ToCreatedResult();

    [HttpPut("{id:guid}")]
    [Authorize(Policy = NwfmPolicies.ManageTaskTypes)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateC2mActionMappingCommand command, CancellationToken ct) =>
        (await sender.Send(command with { Id = id }, ct)).ToActionResult();

    [HttpPut("{id:guid}/status")]
    [Authorize(Policy = NwfmPolicies.ManageTaskTypes)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] SetC2mActionMappingStatusCommand command, CancellationToken ct) =>
        (await sender.Send(command with { Id = id }, ct)).ToActionResult();
}
