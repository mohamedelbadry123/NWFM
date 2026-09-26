using Auth.Application.Common.Interfaces;
using Auth.Application.Users.Models;
using NWFM.Shared.Results;

namespace Auth.Application.Users;

internal static class UserDetailLoader
{
    private static readonly Error NotFound = new("Auth.NotFound", "User not found.");

    public static async Task<Result<UserDetailDto>> LoadAsync(
        IUserAccountService users,
        IAuthDbContext db,
        string userId,
        CancellationToken ct)
    {
        var result = await users.GetUserAsync(userId, ct);
        if (!result.IsSuccess)
            return Result<UserDetailDto>.Failure(NotFound);

        var u = result.Value;
        var scopes = await UserOrgScopeWriter.ListAsync(db, userId, ct);
        return Result<UserDetailDto>.Success(new UserDetailDto
        {
            Id = u.Id,
            UserName = u.UserName,
            Email = u.Email,
            PhoneNumber = u.PhoneNumber,
            IsEnabled = u.IsEnabled,
            TeamId = u.TeamId,
            Roles = u.Roles,
            Scopes = scopes
        });
    }
}
