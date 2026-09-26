using Auth.Application.Auth.Models;
using Auth.Application.Common.Interfaces;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Auth.Commands.ExchangeSsoCode;

internal sealed class ExchangeSsoCodeCommandHandler(IAuthService authService)
    : IRequestHandler<ExchangeSsoCodeCommand, Result<AuthTokenDto>>
{
    public async Task<Result<AuthTokenDto>> Handle(ExchangeSsoCodeCommand request, CancellationToken cancellationToken)
    {
        return await authService.ExchangeSsoCodeAsync(request.Code, cancellationToken);
    }
}
