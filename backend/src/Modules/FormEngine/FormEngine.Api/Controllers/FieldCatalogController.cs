using FormEngine.Api.Common;
using FormEngine.Application.FieldCatalog.Models;
using FormEngine.Application.FieldCatalog.Queries.GetFieldCatalog;
using FormEngine.Application.FieldCatalog.Queries.GetFieldCatalogPaged;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;

namespace FormEngine.Api.Controllers;

/// <summary>
/// The catalog of canonical field names. Reusing a name in a new form reuses its column, which is
/// what keeps one answer comparable across forms.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/form-engine/field-catalog")]
public sealed class FieldCatalogController(ISender sender) : ControllerBase
{
    /// <summary>Autocomplete for the builder's Data Name box.</summary>
    [HttpGet]
    [Authorize(Policy = NwfmPolicies.ViewForms)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<FieldCatalogItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFieldCatalog([FromQuery] GetFieldCatalogQuery query, CancellationToken ct) =>
        (await sender.Send(query, ct)).ToActionResult();

    [HttpGet("paged")]
    [Authorize(Policy = NwfmPolicies.ViewForms)]
    [ProducesResponseType(typeof(Result<PaginatedResult<FieldCatalogItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFieldCatalogPaged([FromQuery] GetFieldCatalogPagedQuery query, CancellationToken ct) =>
        (await sender.Send(query, ct)).ToActionResult();
}
