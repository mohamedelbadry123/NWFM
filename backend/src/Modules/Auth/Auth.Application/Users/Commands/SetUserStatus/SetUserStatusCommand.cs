using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace Auth.Application.Users.Commands.SetUserStatus;

[Authorize(Policy = NwfmPolicies.ManageUsers)]
public sealed record SetUserStatusCommand : IRequest<Result<string>>
{
    public string UserId { get; init; } = default!;
    public bool IsEnabled { get; init; }
}
