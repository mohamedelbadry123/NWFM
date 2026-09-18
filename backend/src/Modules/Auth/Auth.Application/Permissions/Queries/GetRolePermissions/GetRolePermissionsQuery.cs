using Auth.Application.Permissions.Models;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Permissions.Queries.GetRolePermissions;

public sealed record GetRolePermissionsQuery : IRequest<Result<RolePermissionsDto>>
{
    public string RoleName { get; init; } = default!;
}
