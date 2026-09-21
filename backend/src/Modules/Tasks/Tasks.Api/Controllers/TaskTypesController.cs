using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using Tasks.Api.Common;
using Tasks.Application.TaskTypes.Commands.SaveTaskType;
using Tasks.Application.TaskTypes.Models;
using Tasks.Application.TaskTypes.Queries.GetTaskTypes;

namespace Tasks.Api.Controllers;

/// <summary>Task types: which form each kind of field work is filled with.</summary>
[ApiController]
[Authorize]
[Route("api/v1/task-types")]
public sealed class TaskTypesController(ISender sender) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = NwfmPolicies.TaskTypeReaders)]
    [ProducesResponseType(typeof(Result<PaginatedResult<TaskTypeDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTaskTypes([FromQuery] GetTaskTypesQuery query, CancellationToken ct) =>
        (await sender.Send(query, ct)).ToActionResult();

    [HttpGet("active")]
    [Authorize(Policy = NwfmPolicies.TaskTypeReaders)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<TaskTypeDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActive(CancellationToken ct) =>
        (await sender.Send(new GetActiveTaskTypesQuery(), ct)).ToActionResult();

    [HttpGet("form-options")]
    [Authorize(Policy = NwfmPolicies.ManageTaskTypes)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<FormOptionDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFormOptions([FromQuery] string? search, CancellationToken ct) =>
        (await sender.Send(new GetTaskTypeFormOptionsQuery(search), ct)).ToActionResult();

    [HttpGet("{id:guid}")]
    [Authorize(Policy = NwfmPolicies.TaskTypeReaders)]
    [ProducesResponseType(typeof(Result<TaskTypeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTaskType(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetTaskTypeByIdQuery(id), ct)).ToActionResult();

    [HttpPost]
    [Authorize(Policy = NwfmPolicies.ManageTaskTypes)]
    [ProducesResponseType(typeof(Result<TaskTypeDto>), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateTaskTypeCommand command, CancellationToken ct) =>
        (await sender.Send(command, ct)).ToCreatedResult();

    [HttpPut("{id:guid}")]
    [Authorize(Policy = NwfmPolicies.ManageTaskTypes)]
    [ProducesResponseType(typeof(Result<TaskTypeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTaskTypeCommand command, CancellationToken ct) =>
        (await sender.Send(command with { TaskTypeId = id }, ct)).ToActionResult();

    [HttpPut("{id:guid}/status")]
    [Authorize(Policy = NwfmPolicies.ManageTaskTypes)]
    [ProducesResponseType(typeof(Result<TaskTypeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] SetTaskTypeStatusCommand command, CancellationToken ct) =>
        (await sender.Send(command with { TaskTypeId = id }, ct)).ToActionResult();
}
