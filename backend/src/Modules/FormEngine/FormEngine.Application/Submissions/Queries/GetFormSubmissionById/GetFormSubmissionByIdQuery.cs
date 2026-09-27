using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace FormEngine.Application.Submissions.Queries.GetFormSubmissionById;

/// <summary>One submission row, as a column-keyed dictionary.</summary>
[Authorize(Policy = NwfmPolicies.ViewSubmissions)]
public sealed record GetFormSubmissionByIdQuery(Guid FormDefinitionId, Guid SubmissionId)
    : IRequest<Result<IReadOnlyDictionary<string, object?>>>;
