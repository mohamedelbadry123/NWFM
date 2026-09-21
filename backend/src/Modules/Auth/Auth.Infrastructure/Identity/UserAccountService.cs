using Auth.Application.Common.Interfaces;
using Auth.Domain.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Results;

namespace Auth.Infrastructure.Identity;

internal static class AuthErrors
{
    public static readonly Error UserNotFound = new("Auth.UserNotFound", "User not found.");
    public static Error UserAlreadyExists(string userName) =>
        new("Auth.UserAlreadyExists", $"A user named '{userName}' already exists.");
    public static Error IdentityFailure(IdentityResult result) =>
        new("Auth.IdentityError", string.Join(" ", result.Errors.Select(e => e.Description)));
    public static Error UnknownRoles(IEnumerable<string> roles) =>
        new("Auth.UnknownRoles", $"Unknown role(s): {string.Join(", ", roles)}.");
    public static readonly Error CrewRoleNotAllowed =
        new("Auth.CrewRoleNotAllowed", "The FieldTeam role cannot be assigned from user administration.");
    public static readonly Error TeamLoginNotFound = new("Auth.TeamLoginNotFound", "The team has no login.");
}

/// <summary>
/// Account administration over ASP.NET Identity. Every failure comes back as a
/// <see cref="Result{T}"/> carrying Identity's own messages, so a rejected password tells the
/// operator which rule it broke rather than "create failed".
/// </summary>
public sealed class UserAccountService(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager)
    : IUserAccountService
{
    private static readonly DateTimeOffset PermanentLockout = DateTimeOffset.MaxValue;

    public async Task<Result<PaginatedResult<UserAccount>>> GetUsersAsync(
        UserAccountQuery query,
        CancellationToken ct)
    {
        var users = userManager.Users.AsNoTracking();

        if (query.ExcludeFieldTeamAccounts)
            users = users.Where(x => x.TeamId == null);

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim();
            users = users.Where(x =>
                (x.UserName != null && x.UserName.Contains(term)) ||
                (x.Email != null && x.Email.Contains(term)));
        }

        if (query.IsEnabled is bool isEnabled)
        {
            users = isEnabled
                ? users.Where(x => x.LockoutEnd == null || x.LockoutEnd <= DateTimeOffset.UtcNow)
                : users.Where(x => x.LockoutEnd != null && x.LockoutEnd > DateTimeOffset.UtcNow);
        }

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            var roleUserIds = await RoleUserIdsAsync(query.Role.Trim());
            users = users.Where(x => roleUserIds.Contains(x.Id));
        }

        var totalCount = await users.CountAsync(ct);

        var page = await users
            .OrderBy(x => x.UserName)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        var items = new List<UserAccount>(page.Count);
        foreach (var user in page)
            items.Add(await ToAccountAsync(user));

        return Result<PaginatedResult<UserAccount>>.Success(
            new PaginatedResult<UserAccount>(items, totalCount, query.PageNumber, query.PageSize));
    }

    public async Task<Result<UserAccount>> GetUserAsync(string userId, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId);
        return user is null
            ? Result<UserAccount>.Failure(AuthErrors.UserNotFound)
            : Result<UserAccount>.Success(await ToAccountAsync(user));
    }

    public async Task<Result<string>> CreateUserAsync(NewUserAccount account, CancellationToken ct)
    {
        var existing = await userManager.FindByNameAsync(account.UserName);
        if (existing is not null)
            return Result<string>.Failure(AuthErrors.UserAlreadyExists(account.UserName));

        var rolesResult = await ValidateRolesAsync(account.Roles, ct);
        if (rolesResult.IsFailure)
            return Result<string>.Failure(rolesResult.Error);

        var user = new ApplicationUser
        {
            UserName = account.UserName.Trim(),
            Email = string.IsNullOrWhiteSpace(account.Email) ? null : account.Email.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(account.PhoneNumber) ? null : account.PhoneNumber.Trim(),
            TeamId = account.TeamId
        };

        var created = await userManager.CreateAsync(user, account.Password);
        if (!created.Succeeded)
            return Failed(created);

        if (account.Roles.Count > 0)
        {
            var assigned = await userManager.AddToRolesAsync(user, account.Roles);
            if (!assigned.Succeeded)
            {
                await userManager.DeleteAsync(user);
                return Failed(assigned);
            }
        }

        return Result<string>.Success(user.Id.ToString());
    }

    public async Task<Result<string>> UpdateUserAsync(
        string userId, EditedUserAccount account, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return Result<string>.Failure(AuthErrors.UserNotFound);

        var rolesResult = await ValidateRolesAsync(account.Roles, ct);
        if (rolesResult.IsFailure)
            return Result<string>.Failure(rolesResult.Error);

        user.Email = string.IsNullOrWhiteSpace(account.Email) ? null : account.Email.Trim();
        user.PhoneNumber = string.IsNullOrWhiteSpace(account.PhoneNumber) ? null : account.PhoneNumber.Trim();

        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
            return Failed(updated);

        var currentRoles = await userManager.GetRolesAsync(user);
        var removals = currentRoles.Except(account.Roles, StringComparer.Ordinal).ToList();
        var additions = account.Roles.Except(currentRoles, StringComparer.Ordinal).ToList();

        if (removals.Count > 0)
        {
            var removed = await userManager.RemoveFromRolesAsync(user, removals);
            if (!removed.Succeeded) return Failed(removed);
        }

        if (additions.Count > 0)
        {
            var added = await userManager.AddToRolesAsync(user, additions);
            if (!added.Succeeded) return Failed(added);
        }

        return Result<string>.Success(user.Id.ToString());
    }

    public async Task<Result<string>> SetUserEnabledAsync(
        string userId, bool isEnabled, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return Result<string>.Failure(AuthErrors.UserNotFound);

        var enabledLockout = await userManager.SetLockoutEnabledAsync(user, true);
        if (!enabledLockout.Succeeded) return Failed(enabledLockout);

        var result = await userManager.SetLockoutEndDateAsync(user, isEnabled ? null : PermanentLockout);
        return result.Succeeded ? Result<string>.Success(user.Id.ToString()) : Failed(result);
    }

    public async Task<Result<string>> ResetPasswordAsync(
        string userId, string newPassword, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return Result<string>.Failure(AuthErrors.UserNotFound);

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, newPassword);
        return result.Succeeded ? Result<string>.Success(user.Id.ToString()) : Failed(result);
    }

    public async Task<Result<IReadOnlyList<string>>> GetAssignableRolesAsync(CancellationToken ct)
    {
        var roles = await roleManager.Roles
            .AsNoTracking()
            .Where(x => x.Name != null)
            .Select(x => x.Name!)
            .OrderBy(x => x)
            .ToListAsync(ct);

        return Result<IReadOnlyList<string>>.Success(roles);
    }

    private async Task<Result<bool>> ValidateRolesAsync(
        IReadOnlyList<string> roles, CancellationToken ct)
    {
        if (roles.Count == 0)
            return Result<bool>.Success(true);

        if (roles.Contains(Roles.FieldTeam, StringComparer.Ordinal))
            return Result<bool>.Failure(AuthErrors.CrewRoleNotAllowed);

        var known = await roleManager.Roles
            .AsNoTracking()
            .Where(x => x.Name != null)
            .Select(x => x.Name!)
            .ToListAsync(ct);

        var unknown = roles.Except(known, StringComparer.Ordinal).ToList();
        return unknown.Count == 0
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(AuthErrors.UnknownRoles(unknown));
    }

    public async Task<Result<string>> CreateTeamLoginAsync(NewTeamLogin login, CancellationToken ct)
    {
        var userCode = login.UserCode.Trim();

        if (await userManager.FindByNameAsync(userCode) is not null)
            return Result<string>.Failure(AuthErrors.UserAlreadyExists(userCode));

        var user = new ApplicationUser
        {
            UserName = userCode,
            Email = string.IsNullOrWhiteSpace(login.Email) ? null : login.Email.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(login.PhoneNumber) ? null : login.PhoneNumber.Trim(),
            TeamId = login.TeamId
        };

        var created = await userManager.CreateAsync(user, login.Password);
        if (!created.Succeeded)
            return Failed(created);

        var assigned = await userManager.AddToRoleAsync(user, Roles.FieldTeam);
        if (!assigned.Succeeded)
        {
            // An account with a team but no crew role cannot sign in as the crew; do not leave one.
            await userManager.DeleteAsync(user);
            return Failed(assigned);
        }

        return Result<string>.Success(user.Id.ToString());
    }

    public async Task<IReadOnlyDictionary<Guid, TeamLogin>> GetTeamLoginsAsync(
        IReadOnlyCollection<Guid> teamIds, CancellationToken ct)
    {
        if (teamIds.Count == 0)
            return new Dictionary<Guid, TeamLogin>();

        var ids = teamIds.Select(id => (Guid?)id).ToList();

        var users = await userManager.Users
            .AsNoTracking()
            .Where(x => ids.Contains(x.TeamId))
            .ToListAsync(ct);

        return users
            .GroupBy(x => x.TeamId!.Value)
            .ToDictionary(g => g.Key, g => ToTeamLogin(g.OrderBy(u => u.UserName).First()));
    }

    public async Task<Result<string>> UpdateTeamLoginAsync(
        Guid teamId, string? email, string? phoneNumber, bool isEnabled, CancellationToken ct)
    {
        var user = await FindTeamLoginAsync(teamId, ct);
        if (user is null)
            return Result<string>.Failure(AuthErrors.TeamLoginNotFound);

        user.Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        user.PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();

        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
            return Failed(updated);

        return await SetUserEnabledAsync(user.Id.ToString(), isEnabled, ct);
    }

    public async Task<Result<string>> ResetTeamLoginPasswordAsync(Guid teamId, string newPassword, CancellationToken ct)
    {
        var user = await FindTeamLoginAsync(teamId, ct);
        return user is null
            ? Result<string>.Failure(AuthErrors.TeamLoginNotFound)
            : await ResetPasswordAsync(user.Id.ToString(), newPassword, ct);
    }

    private Task<ApplicationUser?> FindTeamLoginAsync(Guid teamId, CancellationToken ct) =>
        userManager.Users.OrderBy(x => x.UserName).FirstOrDefaultAsync(x => x.TeamId == teamId, ct);

    private static TeamLogin ToTeamLogin(ApplicationUser user) => new()
    {
        UserId = user.Id.ToString(),
        UserCode = user.UserName ?? string.Empty,
        Email = user.Email,
        PhoneNumber = user.PhoneNumber,
        IsEnabled = user.LockoutEnd is null || user.LockoutEnd <= DateTimeOffset.UtcNow
    };

    private async Task<HashSet<Guid>> RoleUserIdsAsync(string role)
    {
        var users = await userManager.GetUsersInRoleAsync(role);
        return users.Select(x => x.Id).ToHashSet();
    }

    private async Task<UserAccount> ToAccountAsync(ApplicationUser user) => new()
    {
        Id = user.Id.ToString(),
        UserName = user.UserName ?? string.Empty,
        Email = user.Email,
        PhoneNumber = user.PhoneNumber,
        TeamId = user.TeamId,
        IsEnabled = user.LockoutEnd is null || user.LockoutEnd <= DateTimeOffset.UtcNow,
        Roles = [.. await userManager.GetRolesAsync(user)]
    };

    private static Result<string> Failed(IdentityResult result) =>
        Result<string>.Failure(AuthErrors.IdentityFailure(result));
}
