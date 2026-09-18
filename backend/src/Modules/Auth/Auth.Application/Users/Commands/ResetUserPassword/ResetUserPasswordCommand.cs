using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Users.Commands.ResetUserPassword;

public sealed record ResetUserPasswordCommand : IRequest<Result<string>>
{
    public string UserId { get; init; } = default!;
    public string NewPassword { get; init; } = default!;
}
