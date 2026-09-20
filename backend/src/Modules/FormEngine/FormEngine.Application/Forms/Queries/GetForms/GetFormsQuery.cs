using FormEngine.Application.Forms.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace FormEngine.Application.Forms.Queries.GetForms;

[Authorize(Policy = NwfmPolicies.ViewForms)]
public sealed record GetFormsQuery : IRequest<Result<PaginatedResult<FormListItemDto>>>
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public string? SearchTerm { get; init; }
    public string? Category { get; init; }
    public string? Status { get; init; }
    public string? DepartmentCode { get; init; }

    /// <summary>
    /// Hides archived forms when no <see cref="Status"/> is given, so the grid opens on the working
    /// set. An explicit <see cref="Status"/> always wins — asking for <c>ARCHIVED</c> still returns them.
    /// </summary>
    public bool ExcludeArchived { get; init; }
}
