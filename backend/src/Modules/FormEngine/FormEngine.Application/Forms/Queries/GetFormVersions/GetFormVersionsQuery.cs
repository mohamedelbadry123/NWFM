using FormEngine.Application.Forms.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace FormEngine.Application.Forms.Queries.GetFormVersions;

/// <summary>A form's publish history, newest first, without the schemas.</summary>
[Authorize(Policy = NwfmPolicies.ViewForms)]
public sealed record GetFormVersionsQuery(Guid FormDefinitionId) : IRequest<Result<IReadOnlyList<FormVersionSummaryDto>>>;
