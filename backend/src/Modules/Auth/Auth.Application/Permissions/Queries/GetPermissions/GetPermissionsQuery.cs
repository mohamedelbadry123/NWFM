using Auth.Application.Permissions.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace Auth.Application.Permissions.Queries.GetPermissions;

[Authorize(Policy = NwfmPolicies.CanManageRolePermissions)]
public sealed record GetPermissionsQuery : IRequest<Result<IReadOnlyList<PermissionDto>>>;
