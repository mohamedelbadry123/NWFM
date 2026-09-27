using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace Auth.Application.Users.Commands.ResetUserPassword;

[Authorize(Policy = NwfmPolicies.ManageUsers)]
public sealed record ResetUserPasswordCommand : IRequest<Result<string>>
{
    public string UserId { get; init; } = default!;
    public string NewPassword { get; init; } = default!;
}
