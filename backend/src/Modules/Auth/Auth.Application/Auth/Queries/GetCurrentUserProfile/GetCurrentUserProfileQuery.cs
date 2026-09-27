using Auth.Application.Auth.Models;
using MediatR;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace Auth.Application.Auth.Queries.GetCurrentUserProfile;

[Authorize]
public sealed record GetCurrentUserProfileQuery : IRequest<Result<CurrentUserProfileDto>>;
