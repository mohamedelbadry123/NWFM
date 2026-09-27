using Auth.Application.Users.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace Auth.Application.Users.Queries.GetAssignableRoles;

[Authorize(Policy = NwfmPolicies.ManageUsers)]
public sealed record GetAssignableRolesQuery : IRequest<Result<IReadOnlyList<string>>>;
