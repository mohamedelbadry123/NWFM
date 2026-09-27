using FormEngine.Application.Forms.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace FormEngine.Application.Forms.Queries.GetFormById;

/// <summary>A form with its working schema — what the designer and the details dialog load.</summary>
[Authorize(Policy = NwfmPolicies.ViewForms)]
public sealed record GetFormByIdQuery(Guid Id) : IRequest<Result<FormDetailDto>>;
