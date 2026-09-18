using Auth.Application.Permissions.Models;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Permissions.Queries.GetRoles;

public sealed record GetRolesQuery : IRequest<Result<IReadOnlyList<RoleDto>>>;
