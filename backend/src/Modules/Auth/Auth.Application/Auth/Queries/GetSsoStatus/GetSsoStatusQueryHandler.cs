using Auth.Application.Auth.Models;
using Auth.Domain.Options;
using MediatR;
using Microsoft.Extensions.Options;
using NWFM.Shared.Results;

namespace Auth.Application.Auth.Queries.GetSsoStatus;

internal sealed class GetSsoStatusQueryHandler(IOptions<SsoSettings> ssoSettings)
    : IRequestHandler<GetSsoStatusQuery, Result<SsoStatusDto>>
{
    public Task<Result<SsoStatusDto>> Handle(GetSsoStatusQuery request, CancellationToken cancellationToken)
    {
        SsoSettings settings = ssoSettings.Value;

        var dto = new SsoStatusDto
        {
            Enabled = settings.Enabled,
            AllowLocalLoginForAdministrators = settings.AllowLocalLoginForAdministrators
        };

        return Task.FromResult(Result<SsoStatusDto>.Success(dto));
    }
}
