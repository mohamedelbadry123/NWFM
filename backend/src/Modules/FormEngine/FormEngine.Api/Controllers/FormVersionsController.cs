using FormEngine.Api.Common;
using FormEngine.Application.Forms.Models;
using FormEngine.Application.Forms.Queries.GetFormVersion;
using FormEngine.Application.Forms.Queries.GetFormVersions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;

namespace FormEngine.Api.Controllers;

/// <summary>
/// A form's published versions. Reading one version's schema is open to whoever designs, fills or
/// reviews forms — filling a form means rendering the version it was pinned to.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/form-engine/forms/{formId:guid}/versions")]
public sealed class FormVersionsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = NwfmPolicies.ViewForms)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<FormVersionSummaryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVersions(Guid formId, CancellationToken ct) =>
        (await sender.Send(new GetFormVersionsQuery(formId), ct)).ToActionResult();

    [HttpGet("current")]
    [Authorize(Policy = NwfmPolicies.FormSchemaReaders)]
    [ProducesResponseType(typeof(Result<FormVersionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentVersion(Guid formId, CancellationToken ct) =>
        (await sender.Send(new GetFormVersionQuery(formId, null), ct)).ToActionResult();

    [HttpGet("{versionNo:int}")]
    [Authorize(Policy = NwfmPolicies.FormSchemaReaders)]
    [ProducesResponseType(typeof(Result<FormVersionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVersion(Guid formId, int versionNo, CancellationToken ct) =>
        (await sender.Send(new GetFormVersionQuery(formId, versionNo), ct)).ToActionResult();
}
