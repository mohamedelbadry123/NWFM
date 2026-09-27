using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace FormEngine.Application.Submissions.Queries.GetFormSubmissions;

/// <summary>
/// A form's submissions, newest first. Each row is a column-keyed dictionary: the base columns plus
/// whichever answer columns the form's versions declare, so the shape follows the form.
/// </summary>
[Authorize(Policy = NwfmPolicies.ViewSubmissions)]
public sealed record GetFormSubmissionsQuery : IRequest<Result<PaginatedResult<IReadOnlyDictionary<string, object?>>>>
{
    public Guid FormDefinitionId { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;

    /// <summary>Narrows to one owner, e.g. every fill recorded against a single work item.</summary>
    public string? ContextType { get; init; }

    public string? ContextId { get; init; }
}
