namespace Tenancy.Domain.Entities;

/// <summary>
/// A SQL Server the platform creates managed tenant databases on. The admin connection string
/// (a login with <c>dbcreator</c> and <c>securityadmin</c>) is stored encrypted and used only by provisioning.
/// </summary>
public sealed class DatabaseServer
{
    public const int NameMaxLength = 100;

    private DatabaseServer()
    {
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = default!;

    public string EncryptedAdminConnectionString { get; private set; } = default!;

    /// <summary>Azure SQL / contained databases: create a contained user rather than a server login.</summary>
    public bool UseContainedUsers { get; private set; }

    public int MaxDatabases { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public static DatabaseServer Create(string name, string encryptedAdminConnectionString, bool useContainedUsers,
        int maxDatabases, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("A database server must have a name.");
        }

        if (string.IsNullOrWhiteSpace(encryptedAdminConnectionString))
        {
            throw new DomainException("A database server needs an admin connection string.");
        }

        if (maxDatabases < 1)
        {
            throw new DomainException("A database server must allow at least one database.");
        }

        return new DatabaseServer
        {
            Name = name.Trim(),
            EncryptedAdminConnectionString = encryptedAdminConnectionString,
            UseContainedUsers = useContainedUsers,
            MaxDatabases = maxDatabases,
            IsActive = true,
            CreatedAtUtc = utcNow
        };
    }

    public void Update(string name, bool useContainedUsers, int maxDatabases, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("A database server must have a name.");
        }

        if (maxDatabases < 1)
        {
            throw new DomainException("A database server must allow at least one database.");
        }

        Name = name.Trim();
        UseContainedUsers = useContainedUsers;
        MaxDatabases = maxDatabases;
        IsActive = isActive;
    }

    public void ReplaceAdminConnectionString(string encryptedAdminConnectionString)
    {
        if (string.IsNullOrWhiteSpace(encryptedAdminConnectionString))
        {
            throw new DomainException("A database server needs an admin connection string.");
        }

        EncryptedAdminConnectionString = encryptedAdminConnectionString;
    }
}
