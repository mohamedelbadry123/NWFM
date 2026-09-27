using FormEngine.Application.FieldCatalog.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace FormEngine.Application.FieldCatalog.Queries.GetFieldCatalogPaged;

/// <summary>The field catalog admin grid: every canonical name and the type its column is built on.</summary>
[Authorize(Policy = NwfmPolicies.ViewForms)]
public sealed record GetFieldCatalogPagedQuery : IRequest<Result<PaginatedResult<FieldCatalogItemDto>>>
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public string? SearchTerm { get; init; }
}
