namespace Auth.Domain.Entities;

public sealed class UserRefreshToken
{
    private UserRefreshToken() { }

    private UserRefreshToken(string userId, string tokenHash, DateTime expiresAtUtc, DateTime createdAtUtc)
    {
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAtUtc = expiresAtUtc;
        CreatedAtUtc = createdAtUtc;
    }

    public long Id { get; private set; }
    public string UserId { get; private set; } = default!;
    public string TokenHash { get; private set; } = default!;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }

    public static UserRefreshToken Create(
        string userId, string tokenHash, DateTime expiresAtUtc, DateTime createdAtUtc) =>
        new(userId, tokenHash, expiresAtUtc, createdAtUtc);

    public bool IsActive(DateTime utcNow) =>
        RevokedAtUtc is null && ExpiresAtUtc > utcNow;

    public void Revoke(DateTime utcNow) => RevokedAtUtc = utcNow;
}
