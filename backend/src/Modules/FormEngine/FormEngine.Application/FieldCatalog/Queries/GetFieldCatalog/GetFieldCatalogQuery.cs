using FormEngine.Application.FieldCatalog.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace FormEngine.Application.FieldCatalog.Queries.GetFieldCatalog;

/// <summary>Backs the builder's Data Name autocomplete, so it runs on every keystroke.</summary>
[Authorize(Policy = NwfmPolicies.ViewForms)]
public sealed record GetFieldCatalogQuery : IRequest<Result<IReadOnlyList<FieldCatalogItemDto>>>
{
    /// <summary>Optional case-insensitive filter on data name or label.</summary>
    public string? Search { get; init; }

    public int Take { get; init; } = GetFieldCatalogQueryValidator.DefaultTake;
}
