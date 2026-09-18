using Auth.Application.Permissions.Models;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Permissions.Commands.AssignRolePermissions;

public sealed record AssignRolePermissionsCommand : IRequest<Result<RolePermissionsDto>>
{
    public string RoleName { get; init; } = default!;
    public IReadOnlyList<string> PermissionCodes { get; init; } = [];
}
