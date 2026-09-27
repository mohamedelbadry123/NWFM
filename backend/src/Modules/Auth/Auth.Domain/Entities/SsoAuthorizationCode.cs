namespace Auth.Domain.Entities;

public sealed class SsoAuthorizationCode
{
    private SsoAuthorizationCode() { }

    private SsoAuthorizationCode(
        string userId, string codeHash, string? sessionIndex,
        DateTime expiresAtUtc, DateTime createdAtUtc)
    {
        UserId = userId;
        CodeHash = codeHash;
        SessionIndex = sessionIndex;
        ExpiresAtUtc = expiresAtUtc;
        CreatedAtUtc = createdAtUtc;
    }

    public long Id { get; private set; }
    public string UserId { get; private set; } = default!;
    public string CodeHash { get; private set; } = default!;
    public string? SessionIndex { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ConsumedAtUtc { get; private set; }

    public static SsoAuthorizationCode Create(
        string userId, string codeHash, string? sessionIndex,
        DateTime expiresAtUtc, DateTime createdAtUtc) =>
        new(userId, codeHash, sessionIndex, expiresAtUtc, createdAtUtc);

    public bool IsActive(DateTime utcNow) =>
        ConsumedAtUtc is null && ExpiresAtUtc > utcNow;

    public void Consume(DateTime utcNow) => ConsumedAtUtc = utcNow;
}
