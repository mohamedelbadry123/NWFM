using Auth.Application.Common.Interfaces;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Users.Commands.SetUserStatus;

public sealed class SetUserStatusCommandHandler(IUserAccountService userAccountService)
    : IRequestHandler<SetUserStatusCommand, Result<string>>
{
    public async Task<Result<string>> Handle(SetUserStatusCommand request, CancellationToken ct)
    {
        return await userAccountService.SetUserEnabledAsync(request.UserId, request.IsEnabled, ct);
    }
}
