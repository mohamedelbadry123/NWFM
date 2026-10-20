using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace Auth.Application.Users.Commands.ResetUserPassword;

[Authorize(Policy = NwfmPolicies.ManageUsers)]
public sealed record ResetUserPasswordCommand : IRequest<Result<string>>
{
    // The API supplies this from the route before command validation.
    public string UserId { get; init; } = string.Empty;
    public string NewPassword { get; init; } = default!;
}
