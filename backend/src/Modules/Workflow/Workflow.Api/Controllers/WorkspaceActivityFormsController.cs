using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using Workflow.Application.Workspace;

namespace Workflow.Api.Controllers;

/// <summary>The designer's Form tab: the Form Engine forms filed under an activity's Department + FA Type, and a read-only preview of one.</summary>
[ApiController, Authorize(Policy = NwfmPolicies.ActivityFormReaders), Route("api/workflow/workspace/activity-forms")]
public sealed class WorkspaceActivityFormsController(IWorkspaceActivityForms forms) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(string? departmentCode, string? fieldActivityCode, int pageNumber = 1, int pageSize = 20, CancellationToken ct = default)
        => Reply(await forms.ListAsync(departmentCode, fieldActivityCode, pageNumber, pageSize, ct));

    [HttpGet("{formId:guid}/versions/{versionNo:int}")]
    public async Task<IActionResult> Preview(Guid formId, int versionNo, string? departmentCode, string? fieldActivityCode, CancellationToken ct)
        => Reply(await forms.PreviewAsync(departmentCode, fieldActivityCode, formId, versionNo, ct));

    private IActionResult Reply<T>(Result<T> result) => result.IsSuccess
        ? Ok(result.Value)
        : result.Error == WorkspaceActivityForms.NotFound || result.Error == WorkspaceActivityForms.VersionNotFound
            ? NotFound(new { result.Error.Code, result.Error.Message })
            : BadRequest(new { result.Error.Code, result.Error.Message });
}
