using FormEngine.Application.Forms.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace FormEngine.Application.Forms.Queries.GetPublishedForms;

/// <summary>
/// The forms that can be filled right now, with every version a consumer could pin. Serves the fill
/// picker today and the workflow designer's task-form picker later.
/// </summary>
[Authorize(Policy = NwfmPolicies.FormPickers)]
public sealed record GetPublishedFormsQuery : IRequest<Result<IReadOnlyList<PublishedFormDto>>>
{
    public string? SearchTerm { get; init; }
    public string? Category { get; init; }
    public int Take { get; init; } = GetPublishedFormsQueryValidator.DefaultTake;
}
