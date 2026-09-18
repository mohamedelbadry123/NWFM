using NWFM.Shared.Results;

namespace Auth.Application.Common.Interfaces;

public interface IUserAccountService
{
    Task<Result<PaginatedResult<UserAccount>>> GetUsersAsync(UserAccountQuery query, CancellationToken ct);
    Task<Result<UserAccount>> GetUserAsync(string userId, CancellationToken ct);
    Task<Result<string>> CreateUserAsync(NewUserAccount account, CancellationToken ct);
    Task<Result<string>> UpdateUserAsync(string userId, EditedUserAccount account, CancellationToken ct);
    Task<Result<string>> SetUserEnabledAsync(string userId, bool isEnabled, CancellationToken ct);
    Task<Result<string>> ResetPasswordAsync(string userId, string newPassword, CancellationToken ct);
    Task<Result<IReadOnlyList<string>>> GetAssignableRolesAsync(CancellationToken ct);
}

public sealed record UserAccountQuery
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public string? SearchTerm { get; init; }
    public string? Role { get; init; }
    public bool? IsEnabled { get; init; }
    public bool ExcludeFieldTeamAccounts { get; init; }
}

public sealed record UserAccount
{
    public string Id { get; init; } = default!;
    public string UserName { get; init; } = default!;
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public long? TeamId { get; init; }
    public bool IsEnabled { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
}

public sealed record NewUserAccount
{
    public string UserName { get; init; } = default!;
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public string Password { get; init; } = default!;
    public long? TeamId { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
}

public sealed record EditedUserAccount
{
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
}
