using System.Security.Cryptography;
using System.Text;

namespace Tenancy.Domain.Entities;

/// <summary>
/// One use of a <see cref="SupportAccessGrant"/>: a platform user acting inside the tenant as a
/// tenant account. Never outlives its grant, and every token issued for it carries its id so a
/// revoked grant can be enforced on the next request rather than at token expiry.
/// </summary>
public sealed class SupportSession
{
    public const int TicketMaxLength = 100;

    /// <summary>Hex SHA-256 of the handoff code.</summary>
    public const int HandoffCodeHashLength = 64;

    private const int HandoffCodeBytes = 32;
    private const string HandoffInvalid = "The support handoff link is invalid, expired or already used.";

    private SupportSession()
    {
    }

    public Guid Id { get; private set; }

    public Guid GrantId { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid PlatformUserId { get; private set; }

    public string PlatformUserName { get; private set; } = default!;

    public SupportAccessScope Scope { get; private set; }

    public string TicketReference { get; private set; } = default!;

    /// <summary>Identity user id inside the tenant the session acts as.</summary>
    public string ImpersonatedUserId { get; private set; } = default!;

    public string ImpersonatedUserName { get; private set; } = default!;

    public DateTime StartedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? EndedAtUtc { get; private set; }

    /// <summary>
    /// Hash of the one-time code that carries the session from the platform console to the tenant
    /// app. Only the hash is stored: the code itself travels once, in a URL fragment, and is never
    /// written anywhere the control plane can read it back.
    /// </summary>
    public string? HandoffCodeHash { get; private set; }

    public DateTime? HandoffExpiresAtUtc { get; private set; }

    public DateTime? HandoffRedeemedAtUtc { get; private set; }

    public static SupportSession Start(SupportAccessGrant grant, PlatformUser platformUser, string ticketReference,
        string impersonatedUserId, string impersonatedUserName, int sessionMinutes, DateTime utcNow)
    {
        if (!grant.IsActive(utcNow))
        {
            throw new DomainException("The workspace has not granted support access, or the grant has ended.", TenancyErrorCodes.SupportAccessNotGranted, 403);
        }

        if (string.IsNullOrWhiteSpace(ticketReference))
        {
            throw new DomainException("A support session must reference a ticket.");
        }

        var expires = utcNow.AddMinutes(sessionMinutes);

        return new SupportSession
        {
            Id = Guid.NewGuid(),
            GrantId = grant.Id,
            TenantId = grant.TenantId,
            PlatformUserId = platformUser.Id,
            PlatformUserName = platformUser.DisplayName,
            Scope = grant.Scope,
            TicketReference = ticketReference.Trim(),
            ImpersonatedUserId = impersonatedUserId,
            ImpersonatedUserName = impersonatedUserName,
            StartedAtUtc = utcNow,
            ExpiresAtUtc = expires < grant.ExpiresAtUtc ? expires : grant.ExpiresAtUtc
        };
    }

    public bool IsActive(DateTime utcNow) => EndedAtUtc is null && ExpiresAtUtc > utcNow;

    /// <summary>
    /// Issues the single-use code the tenant app exchanges for its own token. The control plane never
    /// holds the tenant signing key; it hands over this code and the tenant API mints the token.
    /// </summary>
    public string IssueHandoffCode(TimeSpan lifetime, DateTime utcNow)
    {
        if (!IsActive(utcNow))
        {
            throw new DomainException(HandoffInvalid, TenancyErrorCodes.SupportAccessNotGranted, 403);
        }

        if (HandoffCodeHash is not null)
        {
            throw new DomainException("A handoff code has already been issued for this support session.");
        }

        var code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(HandoffCodeBytes))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        HandoffCodeHash = HashHandoffCode(code);
        HandoffExpiresAtUtc = utcNow.Add(lifetime) < ExpiresAtUtc ? utcNow.Add(lifetime) : ExpiresAtUtc;
        return code;
    }

    /// <summary>Spends the handoff code. A second redemption, a late one or a wrong code all fail the same way.</summary>
    public void RedeemHandoff(string code, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(code)
            || HandoffCodeHash is null
            || HandoffRedeemedAtUtc is not null
            || HandoffExpiresAtUtc is null
            || HandoffExpiresAtUtc <= utcNow
            || !IsActive(utcNow)
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(HashHandoffCode(code)),
                Encoding.ASCII.GetBytes(HandoffCodeHash)))
        {
            throw new DomainException(HandoffInvalid, TenancyErrorCodes.SupportAccessNotGranted, 403);
        }

        HandoffRedeemedAtUtc = utcNow;
    }

    public static string HashHandoffCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim())));

    public void End(DateTime utcNow) => EndedAtUtc ??= utcNow;
}
