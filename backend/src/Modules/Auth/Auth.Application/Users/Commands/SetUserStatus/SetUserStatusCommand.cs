using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace Auth.Application.Users.Commands.SetUserStatus;

[Authorize(Policy = NwfmPolicies.ManageUsers)]
public sealed record SetUserStatusCommand : IRequest<Result<string>>
{
    // The API supplies this from the route before command validation.
    public string UserId { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }
}
