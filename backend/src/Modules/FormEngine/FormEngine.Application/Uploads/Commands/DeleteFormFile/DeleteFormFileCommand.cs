using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace FormEngine.Application.Uploads.Commands.DeleteFormFile;

/// <summary>
/// Removes a file the user picked but has not submitted yet. Only the person who uploaded it (or an
/// administrator) may remove it, and only while it is still pending — once a submission references a
/// file, deleting it would leave that row pointing at nothing.
/// </summary>
[Authorize(Policy = NwfmPolicies.FormUploaders)]
public sealed record DeleteFormFileCommand(Guid FileId) : IRequest<Result>;
