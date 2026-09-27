using FormEngine.Application.Forms.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace FormEngine.Application.Forms.Queries.GetFormVersion;

/// <summary>
/// One published version with its frozen schema — what the fill page, a submission viewer and a
/// pinned workflow task render. A null <see cref="VersionNo"/> means the current version.
/// </summary>
[Authorize(Policy = NwfmPolicies.FormSchemaReaders)]
public sealed record GetFormVersionQuery(Guid FormDefinitionId, int? VersionNo) : IRequest<Result<FormVersionDto>>;
