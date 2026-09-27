using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Constants;
using NWFM.Shared.Integration.Workflow;
using Workflow.Application.Workspace;

namespace Workflow.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/workflow/workspace")]
public sealed class WorkflowWorkspaceController(IWorkflowWorkspace workspace, IWorkflowReferenceData references, IWorkflowGroupDirectory groups,
    IAuthorizationService authorization) : WorkflowControllerBase
{
    private bool Administrator => User.IsInRole("Administrator");

    [HttpGet("lookups/{kind}")]
    [Authorize(Policy = NwfmPolicies.ViewWorkflows)]
    public async Task<IActionResult> Lookups(string kind, [FromQuery] string? parentCode, CancellationToken ct)
    {
        if (kind is not ("clusters" or "regions" or "cities" or "departments" or "field-activity-types")) return NotFound();
        return Ok(await references.ListAsync(kind, parentCode, ct));
    }
    [HttpGet("groups")]
    [Authorize(Policy = NwfmPolicies.ManageDefinitions)]
    public async Task<IActionResult> Groups(CancellationToken ct) => Ok((await groups.ListAsync(Context.OrganizationId, ct)).Select(g => new { g.Id, g.Name, g.NameAr }));

    [HttpPost("definitions")]
    [Authorize(Policy = NwfmPolicies.ManageDefinitions)]
    public async Task<IActionResult> Create(WorkspaceDefinitionInput input, CancellationToken ct)
    { var result = await workspace.CreateAsync(input, Context.ActorId, ct); return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error); }

    [HttpGet("catalog")]
    [Authorize(Policy = NwfmPolicies.StartWorkflows)]
    public async Task<IActionResult> Catalog(CancellationToken ct) => Ok(await workspace.CatalogAsync(ct));

    [HttpGet("children")]
    [Authorize(Policy = NwfmPolicies.ManageDefinitions)]
    public async Task<IActionResult> Children(CancellationToken ct) => Ok(await workspace.ChildrenAsync(ct));

    [HttpPost("definitions/{id:guid}/instances")]
    [Authorize(Policy = NwfmPolicies.StartWorkflows)]
    public async Task<IActionResult> Start(Guid id, WorkspaceStartInput input, CancellationToken ct)
    { var result = await workspace.StartAsync(id, input, Context.ActorId, Administrator, ct); return result.IsSuccess ? Ok(new { instanceId = result.Value }) : BadRequest(result.Error); }

    [HttpGet("instances")]
    [Authorize(Policy = NwfmPolicies.ViewInstances)]
    public async Task<IActionResult> List([FromQuery] string? search, CancellationToken ct) => Ok(await workspace.ListAsync(search, ct));

    [HttpGet("instances/{id:guid}")]
    [Authorize(Policy = NwfmPolicies.ViewInstances)]
    public async Task<IActionResult> Get(Guid id, [FromQuery] Guid? demoActorId, CancellationToken ct)
    {
        var result = await workspace.GetAsync(id, Context.ActorId, Administrator, demoActorId, ct);
        if (result.IsFailure) return BadRequest(result.Error);
        var detail = result.Value;
        if (!(await authorization.AuthorizeAsync(User, NwfmPolicies.ClaimTasks)).Succeeded)
            detail = detail with { Tree = detail.Tree.Select(run => run with {
                Activities = run.Activities.Select(activity => activity with {
                    Tasks = activity.Tasks.Select(task => task with { CanAct = false, DisabledReason = "Your account can view this instance but cannot perform task actions." }).ToList()
                }).ToList()
            }).ToList() };
        return Ok(detail);
    }

    [HttpPost("tasks/{id:guid}/actions")]
    [Authorize(Policy = NwfmPolicies.ClaimTasks)]
    public async Task<IActionResult> Act(Guid id, WorkspaceActionInput input, CancellationToken ct)
    { var result = await workspace.ActAsync(id, input, Context.ActorId, Administrator, ct); return result.IsSuccess ? NoContent() : BadRequest(result.Error); }

    [HttpPost("tasks/{id:guid}/comments")]
    [Authorize(Policy = NwfmPolicies.ClaimTasks)]
    public Task<IActionResult> Comment(Guid id, WorkspaceActionInput input, CancellationToken ct) => Act(id, input with { Action = "comment" }, ct);

    [HttpPost("demo/tasks/{id:guid}/actions")]
    [Authorize(Roles = "Administrator", Policy = NwfmPolicies.ClaimTasks)]
    public Task<IActionResult> DemoAction(Guid id, WorkspaceActionInput input, CancellationToken ct) =>
        input.DemoActorId is null ? Task.FromResult<IActionResult>(BadRequest(new { message = "Select a demo user." })) : Act(id, input, ct);
}
