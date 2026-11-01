using NWFM.Shared.Results;

namespace NWFM.Shared.MultiTenancy;

/// <summary>
/// Mints the tenant token for a redeemed support session. Runs in the tenant API, inside the
/// tenant scope, so the tenant signing key never leaves the tenant host. Implemented by the module
/// that owns tenant identities.
/// </summary>
public interface ISupportSessionTokenIssuer
{
    Task<Result<SupportSessionTokenDto>> IssueAsync(SupportSessionTicket session, CancellationToken cancellationToken);
}

/// <summary>What the token issuer needs to know about a redeemed support session.</summary>
public sealed record SupportSessionTicket(
    Guid SessionId,
    Guid TenantId,
    string TenantKey,
    Guid PlatformUserId,
    string PlatformUserName,
    string Scope,
    string ImpersonatedUserId,
    DateTime ExpiresAtUtc);

public sealed record SupportSessionTokenDto
{
    public string AccessToken { get; init; } = default!;

    public int ExpiresInSeconds { get; init; }

    public Guid SessionId { get; init; }

    public Guid TenantId { get; init; }

    public string TenantKey { get; init; } = default!;

    public string SupportScope { get; init; } = default!;

    public string ImpersonatedUserId { get; init; } = default!;

    public string ImpersonatedUserName { get; init; } = default!;

    public string ActorName { get; init; } = default!;

    public IReadOnlyList<string> Roles { get; init; } = [];

    public IReadOnlyList<string> Permissions { get; init; } = [];
}
