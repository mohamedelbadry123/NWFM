using Auth.Application.Auth.Models;
using Auth.Application.Common.Interfaces;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Auth.Commands.RefreshAccessToken;

internal sealed class RefreshAccessTokenCommandHandler(IAuthService authService)
    : IRequestHandler<RefreshAccessTokenCommand, Result<AuthTokenDto>>
{
    public async Task<Result<AuthTokenDto>> Handle(RefreshAccessTokenCommand request, CancellationToken cancellationToken)
    {
        return await authService.RefreshAsync(request.RefreshToken, cancellationToken);
    }
}
