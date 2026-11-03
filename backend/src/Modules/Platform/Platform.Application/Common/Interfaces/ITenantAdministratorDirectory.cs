
namespace Platform.Application.Common.Interfaces;

/// <summary>
/// The back-office administrators inside a tenant database, managed from the console. Runs in a
/// nested tenant scope; the platform request itself never opens the tenant database.
/// </summary>
public interface ITenantAdministratorDirectory
{
    Task<IReadOnlyList<TenantAdministratorDto>> ListAsync(TenantInfo tenant, CancellationToken cancellationToken);

    /// <summary>Creates an Administrator with no password yet. The plan's user limit applies. Returns the user id.</summary>
    Task<Result<string>> CreateAsync(TenantInfo tenant, string email, string? phoneNumber, CancellationToken cancellationToken);

    /// <summary>Suspends (permanent lockout) or reactivates an administrator. Never suspends the last active one.</summary>
    Task<Result<TenantAdministratorDto>> SetEnabledAsync(TenantInfo tenant, string userId, bool isEnabled, CancellationToken cancellationToken);

    /// <summary>
    /// Gives the administrator a new temporary password (changed at first sign-in) and a set-password
    /// link, and e-mails both. With e-mail switched off the account is left untouched.
    /// </summary>
    Task<Result<TenantCredentialsDelivery>> SendCredentialsAsync(TenantCredentialsRequest request, CancellationToken cancellationToken);
}

public sealed record TenantAdministratorDto
{
    public string UserId { get; init; } = default!;

    public string UserName { get; init; } = default!;

    public string? Email { get; init; }

    public string? PhoneNumber { get; init; }

    /// <summary>See <see cref="TenantAdministratorStatuses"/>.</summary>
    public string Status { get; init; } = default!;

    public DateTimeOffset? CreatedAt { get; init; }
}

public static class TenantAdministratorStatuses
{
    public const string Active = "Active";
    public const string Suspended = "Suspended";

    /// <summary>Has not chosen a password yet: still on the temporary one, or never sent one.</summary>
    public const string PendingActivation = "PendingActivation";
}

/// <summary>Which wording the credentials e-mail uses.</summary>
public enum TenantCredentialsEmail
{
    /// <summary>The first administrator of a workspace that was just created.</summary>
    NewWorkspace = 1,

    /// <summary>An administrator added later, or whose details were sent again.</summary>
    Administrator = 2
}

public sealed record TenantCredentialsRequest(
    TenantInfo Tenant,
    string UserId,
    string Language,
    string OrganizationName,
    TenantCredentialsEmail Email);

public enum TenantCredentialsDelivery
{
    Sent = 1,

    /// <summary>Written to the mock mail folder (development).</summary>
    WrittenToMockFolder = 2,

    /// <summary>The e-mail channel is off: nothing was sent and the account was not changed.</summary>
    EmailDisabled = 3
}
