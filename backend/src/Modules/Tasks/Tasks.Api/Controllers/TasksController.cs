using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using Tasks.Api.Common;
using Tasks.Application.Tasks.Commands.AssignTask;
using Tasks.Application.Tasks.Commands.CompleteTask;
using Tasks.Application.Tasks.Commands.CreateTask;
using Tasks.Application.Tasks.Commands.ExpireTask;
using Tasks.Application.Tasks.Commands.ReturnTask;
using Tasks.Application.Tasks.Commands.SubmitTaskFill;
using Tasks.Application.Tasks.Commands.UpdateTask;
using Tasks.Application.Tasks.Models;
using Tasks.Application.Tasks.Queries.GetEligibleTeams;
using Tasks.Application.Tasks.Queries.GetTaskActivity;
using Tasks.Application.Tasks.Queries.GetTaskById;
using Tasks.Application.Tasks.Queries.GetTasks;

namespace Tasks.Api.Controllers;

/// <summary>Field tasks: the worklist, the lifecycle, and the fills.</summary>
[ApiController]
[Authorize]
[Route("api/v1/tasks")]
public sealed class TasksController(ISender sender) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = NwfmPolicies.ViewTasks)]
    [ProducesResponseType(typeof(Result<PaginatedResult<TaskListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTasks([FromQuery] GetTasksQuery query, CancellationToken ct) =>
        (await sender.Send(query, ct)).ToActionResult();

    [HttpGet("{id:guid}")]
    [Authorize(Policy = NwfmPolicies.ViewTasks)]
    [ProducesResponseType(typeof(Result<TaskDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTask(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetTaskByIdQuery(id), ct)).ToActionResult();

    [HttpGet("{id:guid}/timeline")]
    [Authorize(Policy = NwfmPolicies.ViewTasks)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<TaskHistoryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTimeline(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetTaskTimelineQuery(id), ct)).ToActionResult();

    [HttpGet("{id:guid}/fills")]
    [Authorize(Policy = NwfmPolicies.ViewTasks)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<TaskFillDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFills(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetTaskFillsQuery(id), ct)).ToActionResult();

    [HttpGet("{id:guid}/files")]
    [Authorize(Policy = NwfmPolicies.ViewTasks)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<TaskFileDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFiles(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetTaskFilesQuery(id), ct)).ToActionResult();

    [HttpGet("{id:guid}/eligible-teams")]
    [Authorize(Policy = NwfmPolicies.AssignTasks)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<EligibleTeamDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEligibleTeams(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetEligibleTeamsQuery(id), ct)).ToActionResult();

    [HttpPost]
    [Authorize(Policy = NwfmPolicies.ManageTasks)]
    [ProducesResponseType(typeof(Result<Guid>), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateTask([FromBody] CreateTaskCommand command, CancellationToken ct) =>
        (await sender.Send(command, ct)).ToCreatedResult();

    [HttpPut("{id:guid}")]
    [Authorize(Policy = NwfmPolicies.ManageTasks)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTask(Guid id, [FromBody] UpdateTaskCommand command, CancellationToken ct) =>
        (await sender.Send(command with { TaskId = id }, ct)).ToActionResult();

    [HttpPost("{id:guid}/assign")]
    [Authorize(Policy = NwfmPolicies.AssignTasks)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status200OK)]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignTaskCommand command, CancellationToken ct) =>
        (await sender.Send(command with { TaskId = id }, ct)).ToActionResult();

    /// <summary>
    /// Records a fill. A new fill answers 201; a retry under a client key already recorded answers 200
    /// with the original submission, so a client can resend after losing a reply.
    /// </summary>
    [HttpPost("{id:guid}/fill")]
    [Authorize(Policy = NwfmPolicies.SubmitTasks)]
    [ProducesResponseType(typeof(Result<TaskFillResultDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Result<TaskFillResultDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Fill(Guid id, [FromBody] SubmitTaskFillCommand command, CancellationToken ct)
    {
        var result = await sender.Send(command with { TaskId = id }, ct);

        return result.IsSuccess && !result.Value.IsReplay ? result.ToCreatedResult() : result.ToActionResult();
    }

    [HttpPost("{id:guid}/complete")]
    [Authorize(Policy = NwfmPolicies.ReviewTasks)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status200OK)]
    public async Task<IActionResult> Complete(Guid id, [FromBody] CompleteTaskCommand command, CancellationToken ct) =>
        (await sender.Send(command with { TaskId = id }, ct)).ToActionResult();

    [HttpPost("{id:guid}/return")]
    [Authorize(Policy = NwfmPolicies.ReviewTasks)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status200OK)]
    public async Task<IActionResult> Return(Guid id, [FromBody] ReturnTaskCommand command, CancellationToken ct) =>
        (await sender.Send(command with { TaskId = id }, ct)).ToActionResult();

    [HttpPost("{id:guid}/expire")]
    [Authorize(Policy = NwfmPolicies.ManageTasks)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status200OK)]
    public async Task<IActionResult> Expire(Guid id, [FromBody] ExpireTaskCommand command, CancellationToken ct) =>
        (await sender.Send(command with { TaskId = id }, ct)).ToActionResult();

    [HttpPost("{id:guid}/migrate-version")]
    [Authorize(Policy = NwfmPolicies.ManageTasks)]
    [ProducesResponseType(typeof(Result<int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MigrateVersion(Guid id, CancellationToken ct) =>
        (await sender.Send(new MigrateTaskFormVersionCommand(id), ct)).ToActionResult();
}
