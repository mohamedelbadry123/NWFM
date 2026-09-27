using Auth.Application.Users.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace Auth.Application.Users.Commands.CreateUser;

[Authorize(Policy = NwfmPolicies.ManageUsers)]
public sealed record CreateUserCommand : IRequest<Result<UserDetailDto>>
{
    public string UserName { get; init; } = default!;
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public string Password { get; init; } = default!;
    public IReadOnlyList<string> Roles { get; init; } = [];
    public IReadOnlyList<OrgScopeAssignmentDto> Scopes { get; init; } = [];
}
