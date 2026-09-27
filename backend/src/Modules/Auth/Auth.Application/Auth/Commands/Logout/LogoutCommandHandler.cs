using Auth.Application.Common.Interfaces;
using Auth.Application.Constants;
using MediatR;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Results;

namespace Auth.Application.Auth.Commands.Logout;

internal sealed class LogoutCommandHandler(IAuthService authService, ICurrentUser currentUser)
    : IRequestHandler<LogoutCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentUser.Id))
            return Result<bool>.Failure(AuthErrors.Unauthorized);

        return await authService.LogoutAsync(currentUser.Id, request.RefreshToken, cancellationToken);
    }
}
