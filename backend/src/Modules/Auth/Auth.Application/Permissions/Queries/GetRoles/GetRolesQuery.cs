using Auth.Application.Permissions.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace Auth.Application.Permissions.Queries.GetRoles;

[Authorize(Policy = NwfmPolicies.CanManageRolePermissions)]
public sealed record GetRolesQuery : IRequest<Result<IReadOnlyList<RoleDto>>>;
