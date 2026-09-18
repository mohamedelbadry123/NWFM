using Auth.Application.Users.Models;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Users.Commands.UpdateUser;

public sealed record UpdateUserCommand : IRequest<Result<UserDetailDto>>
{
    public string UserId { get; init; } = default!;
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
}
