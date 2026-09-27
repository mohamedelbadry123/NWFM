namespace Workflow.Api.Controllers;

using NWFM.Shared.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Workflow.Application.DTOs;
using Workflow.Application.Queries.GetWorkloadDashboard;

/// <summary>
/// Aggregated work-item workload dashboard for assignment groups.
/// </summary>
[ApiController]
[Route("api/workflow/workload")]
[Produces("application/json")]
[Authorize(Policy = NwfmPolicies.ViewWorkload)]
public sealed class WorkflowWorkloadController : WorkflowControllerBase
{
    private readonly ISender _sender;

    public WorkflowWorkloadController(ISender sender) => _sender = sender;

    private Guid GetOrgId() =>
        Context.OrganizationId;

    /// <summary>Returns pending/overdue/claimed counts by assignment group plus org totals.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(WorkloadDashboardDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetWorkloadDashboardQuery(GetOrgId()), cancellationToken);
        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }
}
