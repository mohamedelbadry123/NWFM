using Auth.Application.Permissions.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace Auth.Application.Permissions.Commands.AssignRolePermissions;

[Authorize(Policy = NwfmPolicies.CanManageRolePermissions)]
public sealed record AssignRolePermissionsCommand : IRequest<Result<RolePermissionsDto>>
{
    public string RoleName { get; init; } = default!;
    public IReadOnlyList<string> PermissionCodes { get; init; } = [];
}
