using Auth.Application.Common.Interfaces;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Users.Commands.ResetUserPassword;

public sealed class ResetUserPasswordCommandHandler(IUserAccountService userAccountService)
    : IRequestHandler<ResetUserPasswordCommand, Result<string>>
{
    public async Task<Result<string>> Handle(ResetUserPasswordCommand request, CancellationToken ct)
    {
        return await userAccountService.ResetPasswordAsync(request.UserId, request.NewPassword, ct);
    }
}
