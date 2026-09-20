using Auth.Application.Users.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace Auth.Application.Users.Queries.GetUsers;

[Authorize(Policy = NwfmPolicies.ManageUsers)]
public sealed record GetUsersQuery : IRequest<Result<PaginatedResult<UserListItemDto>>>
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public string? SearchTerm { get; init; }
    public string? Role { get; init; }
    public bool? IsEnabled { get; init; }
}
