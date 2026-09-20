using Auth.Application.Users.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace Auth.Application.Users.Commands.UpdateUser;

[Authorize(Policy = NwfmPolicies.ManageUsers)]
public sealed record UpdateUserCommand : IRequest<Result<UserDetailDto>>
{
    public string UserId { get; init; } = default!;
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
    public IReadOnlyList<OrgScopeAssignmentDto> Scopes { get; init; } = [];
}
