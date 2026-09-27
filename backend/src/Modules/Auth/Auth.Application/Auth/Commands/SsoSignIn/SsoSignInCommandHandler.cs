using Auth.Application.Auth.Models;
using Auth.Application.Common.Interfaces;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Auth.Commands.SsoSignIn;

internal sealed class SsoSignInCommandHandler(IAuthService authService)
    : IRequestHandler<SsoSignInCommand, Result<SsoSignInResultDto>>
{
    public async Task<Result<SsoSignInResultDto>> Handle(SsoSignInCommand request, CancellationToken cancellationToken)
    {
        return await authService.SignInWithSamlAsync(request.NameId, request.SessionIndex, cancellationToken);
    }
}
