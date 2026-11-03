using Auth.Application.Common.Interfaces;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Auth.Commands.ActivateAccount;

internal sealed class ActivateAccountCommandHandler(IUserAccountService accountService)
    : IRequestHandler<ActivateAccountCommand, Result<string>>
{
    public Task<Result<string>> Handle(ActivateAccountCommand request, CancellationToken cancellationToken) =>
        accountService.CompletePasswordSetupAsync(request.UserId, request.Token, request.NewPassword, cancellationToken);
}
