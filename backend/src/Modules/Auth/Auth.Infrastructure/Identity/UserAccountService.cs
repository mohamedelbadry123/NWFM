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
