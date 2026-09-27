using Auth.Application.Permissions.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace Auth.Application.Permissions.Queries.GetRolePermissions;

[Authorize(Policy = NwfmPolicies.CanManageRolePermissions)]
public sealed record GetRolePermissionsQuery : IRequest<Result<RolePermissionsDto>>
{
    public string RoleName { get; init; } = default!;
}
