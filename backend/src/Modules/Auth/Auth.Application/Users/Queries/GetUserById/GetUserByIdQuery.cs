using Auth.Application.Users.Models;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Users.Queries.GetUserById;

public sealed record GetUserByIdQuery : IRequest<Result<UserDetailDto>>
{
    public string UserId { get; init; } = default!;
}
