
namespace Platform.Application.Common.Interfaces;

/// <summary>
/// Reads tenant accounts and writes the tenant audit trail for a support session. Runs inside a
/// tenant scope; the platform request itself never opens the tenant database. It never mints a
/// tenant token — that happens in the tenant API when the handoff code is exchanged.
/// </summary>
public interface ISupportTenantDirectory
{
    Task<Result<SupportImpersonationCandidateDto>> ResolveUserAsync(
        TenantInfo tenant,
        string? impersonatedUserId,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<SupportImpersonationCandidateDto>>> ListCandidatesAsync(
        TenantInfo tenant,
        string? searchTerm,
        CancellationToken cancellationToken);
}

public sealed record SupportImpersonationCandidateDto
{
    public string UserId { get; init; } = default!;

    public string UserName { get; init; } = default!;

    public string? Email { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = [];
}

/// <summary>What the console needs to open the tenant app as the support session.</summary>
public sealed record SupportSessionHandoffDto
{
    public Guid SessionId { get; init; }

    public Guid TenantId { get; init; }

    public string TenantKey { get; init; } = default!;

    /// <summary>The tenant app URL carrying the one-time code in its fragment.</summary>
    public string HandoffUrl { get; init; } = default!;

    public DateTime HandoffExpiresAtUtc { get; init; }

    public DateTime SessionExpiresAtUtc { get; init; }
}
