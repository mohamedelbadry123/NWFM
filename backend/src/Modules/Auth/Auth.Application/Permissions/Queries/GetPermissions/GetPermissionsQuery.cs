using Auth.Application.Permissions.Models;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Permissions.Queries.GetPermissions;

public sealed record GetPermissionsQuery : IRequest<Result<IReadOnlyList<PermissionDto>>>;
