using Auth.Application.Common.Interfaces;
using Auth.Application.Users.Models;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Users.Queries.GetUsers;

public sealed class GetUsersQueryHandler(IUserAccountService userAccountService)
    : IRequestHandler<GetUsersQuery, Result<PaginatedResult<UserListItemDto>>>
{
    public async Task<Result<PaginatedResult<UserListItemDto>>> Handle(GetUsersQuery request, CancellationToken ct)
    {
        var result = await userAccountService.GetUsersAsync(
            new UserAccountQuery
            {
                PageNumber = request.PageNumber,
                PageSize = request.PageSize,
                SearchTerm = request.SearchTerm,
                Role = request.Role,
                IsEnabled = request.IsEnabled,
                ExcludeFieldTeamAccounts = true
            }, ct);

        if (!result.IsSuccess)
            return Result<PaginatedResult<UserListItemDto>>.Failure(result.Error);

        var items = result.Value.Items.Select(u => new UserListItemDto
        {
            Id = u.Id, UserName = u.UserName, Email = u.Email, PhoneNumber = u.PhoneNumber,
            IsEnabled = u.IsEnabled, Roles = u.Roles
        }).ToList();

        return Result<PaginatedResult<UserListItemDto>>.Success(
            new PaginatedResult<UserListItemDto>(items, result.Value.TotalCount, result.Value.PageNumber, result.Value.PageSize));
    }
}
