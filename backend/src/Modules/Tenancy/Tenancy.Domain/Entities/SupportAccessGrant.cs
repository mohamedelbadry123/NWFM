namespace Tenancy.Domain.Entities;

/// <summary>
/// A tenant's consent for platform support to enter its workspace: time-boxed, scoped and revocable.
/// Created and revoked by a tenant administrator; platform staff can only use one that exists.
/// </summary>
public sealed class SupportAccessGrant
{
    public const int ReasonMaxLength = 1000;
    public const int UserNameMaxLength = 256;

    private SupportAccessGrant()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public SupportAccessScope Scope { get; private set; }

    public string Reason { get; private set; } = default!;

    public string GrantedByUserId { get; private set; } = default!;

    public string GrantedByUserName { get; private set; } = default!;

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? RevokedAtUtc { get; private set; }

    public string? RevokedByUserId { get; private set; }

    public static SupportAccessGrant Create(Guid tenantId, SupportAccessScope scope, string reason, int hours,
        int maxHours, string grantedByUserId, string grantedByUserName, DateTime utcNow)
    {
        if (hours < 1 || hours > maxHours)
        {
            throw new DomainException($"Support access lasts between 1 and {maxHours} hours.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Say why support access is being granted.");
        }

        return new SupportAccessGrant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Scope = scope,
            Reason = reason.Length > ReasonMaxLength ? reason[..ReasonMaxLength] : reason.Trim(),
            GrantedByUserId = grantedByUserId,
            GrantedByUserName = grantedByUserName,
            CreatedAtUtc = utcNow,
            ExpiresAtUtc = utcNow.AddHours(hours)
        };
    }

    public bool IsActive(DateTime utcNow) => RevokedAtUtc is null && ExpiresAtUtc > utcNow;

    public void Revoke(string revokedByUserId, DateTime utcNow)
    {
        if (RevokedAtUtc is not null)
        {
            return;
        }

        RevokedAtUtc = utcNow;
        RevokedByUserId = revokedByUserId;
    }
}
