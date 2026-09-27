using FormEngine.Application.Uploads.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace FormEngine.Application.Uploads.Queries.GetFormFile;

/// <summary>
/// Streams an uploaded media file back by its handle. Used while the form is being filled (preview)
/// and when reviewing a submitted row.
/// </summary>
[Authorize(Policy = NwfmPolicies.SubmitOrViewSubmissions)]
public sealed record GetFormFileQuery(Guid FileId) : IRequest<Result<FormFileContentDto>>;
