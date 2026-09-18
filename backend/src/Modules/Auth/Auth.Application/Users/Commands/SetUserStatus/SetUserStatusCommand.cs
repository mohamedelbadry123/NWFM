using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Users.Commands.SetUserStatus;

public sealed record SetUserStatusCommand : IRequest<Result<string>>
{
    public string UserId { get; init; } = default!;
    public bool IsEnabled { get; init; }
}
