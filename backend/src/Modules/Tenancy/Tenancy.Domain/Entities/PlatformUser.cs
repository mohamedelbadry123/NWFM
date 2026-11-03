namespace Tenancy.Domain.Entities;

/// <summary>
/// Platform staff (operators and support engineers). Lives only in the catalog and signs in through
/// the platform token scheme; it is never a user in any tenant database.
/// </summary>
public sealed class PlatformUser
{
    public const int EmailMaxLength = 256;
    public const int NameMaxLength = 200;
    public const int MaxFailedLogins = 5;
    public const int LockoutMinutes = 15;

    private PlatformUser()
    {
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; } = default!;

    public string NormalizedEmail { get; private set; } = default!;

    public string DisplayName { get; private set; } = default!;

    public string PasswordHash { get; private set; } = default!;

    /// <summary>One of <c>PlatformRoles</c>.</summary>
    public string Role { get; private set; } = default!;

    public bool IsActive { get; private set; }

    public int FailedLoginCount { get; private set; }

    public DateTime? LockoutEndUtc { get; private set; }

    public DateTime? LastLoginAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    public static PlatformUser Create(string email, string displayName, string role, string passwordHash, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new DomainException("A platform user needs an e-mail address.");
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("A platform user needs a password.");
        }

        return new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = email.Trim(),
            NormalizedEmail = NormalizeEmail(email),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? email.Trim() : displayName.Trim(),
            Role = RequireRole(role),
            PasswordHash = passwordHash,
            IsActive = true,
            CreatedAtUtc = utcNow
        };
    }

    public void Update(string displayName, string role, bool isActive)
    {
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? Email : displayName.Trim();
        Role = RequireRole(role);
        IsActive = isActive;
    }

    public void ChangePasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("A platform user needs a password.");
        }

        PasswordHash = passwordHash;
        FailedLoginCount = 0;
        LockoutEndUtc = null;
    }

    public bool IsLockedOut(DateTime utcNow) => LockoutEndUtc is { } end && end > utcNow;

    public void RecordFailedLogin(DateTime utcNow)
    {
        FailedLoginCount++;

        if (FailedLoginCount >= MaxFailedLogins)
        {
            LockoutEndUtc = utcNow.AddMinutes(LockoutMinutes);
            FailedLoginCount = 0;
        }
    }

    public void RecordSuccessfulLogin(DateTime utcNow)
    {
        FailedLoginCount = 0;
        LockoutEndUtc = null;
        LastLoginAtUtc = utcNow;
    }

    private static string RequireRole(string role)
    {
        if (!Constants.PlatformRoles.IsDefined(role))
        {
            throw new DomainException("The platform role is not recognised.");
        }

        return role;
    }
}
