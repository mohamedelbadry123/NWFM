using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Auth.Commands.ActivateAccount;

/// <summary>
/// Sets the password of an account from the link in its activation e-mail: how a new workspace's
/// administrator signs in for the first time. Anonymous; the workspace comes from the host.
/// </summary>
public sealed record ActivateAccountCommand : IRequest<Result<string>>
{
    public const int PasswordMaxLength = 128;

    public string UserId { get; init; } = default!;

    public string Token { get; init; } = default!;

    public string NewPassword { get; init; } = default!;
}
