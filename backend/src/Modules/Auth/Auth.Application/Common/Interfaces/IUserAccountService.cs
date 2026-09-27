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

    /// <summary>
    /// Creates the login a field team signs in with: an account on the team, holding the
    /// <c>FieldTeam</c> role. The one path that may grant that role — user administration refuses it,
    /// so a crew login cannot be made by hand and left without a team.
    /// </summary>
    Task<Result<string>> CreateTeamLoginAsync(NewTeamLogin login, CancellationToken ct);

    /// <summary>Each team's login, keyed by team id. A team without one is simply absent.</summary>
    Task<IReadOnlyDictionary<Guid, TeamLogin>> GetTeamLoginsAsync(IReadOnlyCollection<Guid> teamIds, CancellationToken ct);

    /// <summary>Updates the team login's contact details and whether it may sign in.</summary>
    Task<Result<string>> UpdateTeamLoginAsync(Guid teamId, string? email, string? phoneNumber, bool isEnabled, CancellationToken ct);

    Task<Result<string>> ResetTeamLoginPasswordAsync(Guid teamId, string newPassword, CancellationToken ct);
}

public sealed record NewTeamLogin
{
    /// <summary>The code the crew signs in with — the account's user name.</summary>
    public string UserCode { get; init; } = default!;
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public string Password { get; init; } = default!;
    public Guid TeamId { get; init; }
}

public sealed record TeamLogin
{
    public string UserId { get; init; } = default!;
    public string UserCode { get; init; } = default!;
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public bool IsEnabled { get; init; }
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
    public Guid? TeamId { get; init; }
    public bool IsEnabled { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
}

public sealed record NewUserAccount
{
    public string UserName { get; init; } = default!;
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public string Password { get; init; } = default!;
    public Guid? TeamId { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
}

public sealed record EditedUserAccount
{
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
}
