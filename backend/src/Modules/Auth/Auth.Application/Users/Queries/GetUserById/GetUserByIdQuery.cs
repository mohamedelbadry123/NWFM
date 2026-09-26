using Auth.Application.Users.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace Auth.Application.Users.Queries.GetUserById;

[Authorize(Policy = NwfmPolicies.ManageUsers)]
public sealed record GetUserByIdQuery : IRequest<Result<UserDetailDto>>
{
    public string UserId { get; init; } = default!;
}
