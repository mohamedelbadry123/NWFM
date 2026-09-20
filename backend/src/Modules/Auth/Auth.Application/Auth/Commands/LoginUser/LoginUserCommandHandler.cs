using Auth.Application.Auth.Models;
using Auth.Application.Common.Interfaces;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Auth.Commands.LoginUser;

internal sealed class LoginUserCommandHandler(IAuthService authService)
    : IRequestHandler<LoginUserCommand, Result<AuthTokenDto>>
{
    public async Task<Result<AuthTokenDto>> Handle(LoginUserCommand request, CancellationToken cancellationToken)
    {
        return await authService.LoginAsync(request.UserName, request.Password, cancellationToken);
    }
}
