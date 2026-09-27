using Auth.Application.Auth.Models;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Auth.Commands.SsoSignIn;

public sealed record SsoSignInCommand : IRequest<Result<SsoSignInResultDto>>
{
    public string NameId { get; init; } = default!;
    public string? SessionIndex { get; init; }
}
