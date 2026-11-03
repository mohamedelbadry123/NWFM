namespace Tenancy.Domain.Entities;

/// <summary>
/// Where a tenant's data lives. One row per tenant. A provisioned database carries two encrypted
/// credentials: the owner the control plane migrates with, and the restricted runtime principal the
/// tenant API serves requests with (never an owner, never able to reach another database). The
/// <see cref="TenantDatabaseMode.Configuration"/> mode stores only a configuration key name, which
/// each host resolves with its own credential.
/// </summary>
public sealed class TenantDatabase
{
    public const int NameMaxLength = 128;
    public const int ConnectionStringNameMaxLength = 128;
    public const int ErrorMaxLength = 2000;

    /// <summary>Room for one <c>Module=MigrationId</c> pair per module (see <c>TenantSchema</c>).</summary>
    public const int SchemaVersionMaxLength = 1000;

    private TenantDatabase()
    {
    }

    public Guid TenantId { get; private set; }

    public TenantDatabaseMode Mode { get; private set; }

    public int? DatabaseServerId { get; private set; }

    public string? DatabaseName { get; private set; }

    /// <summary>The owner principal the control plane migrates and seeds with.</summary>
    public string? LoginName { get; private set; }

    /// <summary>The restricted principal the tenant API connects as; granted CRUD on the module schemas only.</summary>
    public string RuntimePrincipal { get; private set; } = default!;

    /// <summary>The runtime principal's connection string, encrypted. Read by the tenant API.</summary>
    public string? EncryptedConnectionString { get; private set; }

    /// <summary>The owner's connection string, encrypted. Read by the control plane only.</summary>
    public string? EncryptedAdminConnectionString { get; private set; }

    public string? ConnectionStringName { get; private set; }

    /// <summary>Composite of the last migration applied per module, as recorded by the migration runner.</summary>
    public string? SchemaVersion { get; private set; }

    public DateTime? LastMigratedAtUtc { get; private set; }

    public string? LastMigrationError { get; private set; }

    /// <summary>When the runtime principal last passed the isolation verification.</summary>
    public DateTime? VerifiedAtUtc { get; private set; }

    public static TenantDatabase CreateManaged(Guid tenantId, int databaseServerId, string databaseName, string ownerName,
        string runtimePrincipal, string encryptedRuntimeConnectionString, string encryptedAdminConnectionString)
    {
        Require(databaseName, "A managed database needs a name.");
        Require(runtimePrincipal, "A managed database needs a runtime principal.");
        Require(encryptedRuntimeConnectionString, "A managed database needs a runtime connection string.");
        Require(encryptedAdminConnectionString, "A managed database needs an owner connection string.");

        return new TenantDatabase
        {
            TenantId = tenantId,
            Mode = TenantDatabaseMode.Managed,
            DatabaseServerId = databaseServerId,
            DatabaseName = databaseName,
            LoginName = ownerName,
            RuntimePrincipal = runtimePrincipal,
            EncryptedConnectionString = encryptedRuntimeConnectionString,
            EncryptedAdminConnectionString = encryptedAdminConnectionString
        };
    }

    public static TenantDatabase CreateBringYourOwn(Guid tenantId, string? databaseName, string ownerName,
        string runtimePrincipal, string encryptedRuntimeConnectionString, string encryptedAdminConnectionString)
    {
        Require(runtimePrincipal, "A customer database needs a runtime principal.");
        Require(encryptedRuntimeConnectionString, "A customer database needs a runtime connection string.");
        Require(encryptedAdminConnectionString, "A customer database needs an owner connection string.");

        return new TenantDatabase
        {
            TenantId = tenantId,
            Mode = TenantDatabaseMode.BringYourOwn,
            DatabaseName = databaseName,
            LoginName = ownerName,
            RuntimePrincipal = runtimePrincipal,
            EncryptedConnectionString = encryptedRuntimeConnectionString,
            EncryptedAdminConnectionString = encryptedAdminConnectionString
        };
    }

    public static TenantDatabase CreateFromConfiguration(Guid tenantId, string connectionStringName, string runtimePrincipal)
    {
        Require(connectionStringName, "A configured database needs a connection string name.");
        Require(runtimePrincipal, "A configured database needs a runtime principal.");

        return new TenantDatabase
        {
            TenantId = tenantId,
            Mode = TenantDatabaseMode.Configuration,
            ConnectionStringName = connectionStringName,
            RuntimePrincipal = runtimePrincipal
        };
    }

    public void ReplaceConnectionStrings(string encryptedRuntimeConnectionString, string encryptedAdminConnectionString)
    {
        if (Mode == TenantDatabaseMode.Configuration)
        {
            throw new DomainException("A configured database's connection string is changed in configuration, not here.");
        }

        Require(encryptedRuntimeConnectionString, "A runtime connection string is required.");
        Require(encryptedAdminConnectionString, "An owner connection string is required.");
        EncryptedConnectionString = encryptedRuntimeConnectionString;
        EncryptedAdminConnectionString = encryptedAdminConnectionString;
        VerifiedAtUtc = null;
    }

    public void RecordMigration(string? schemaVersion, DateTime utcNow)
    {
        SchemaVersion = schemaVersion;
        LastMigratedAtUtc = utcNow;
        LastMigrationError = null;
    }

    public void RecordMigrationFailure(string error, DateTime utcNow)
    {
        LastMigratedAtUtc = utcNow;
        LastMigrationError = error.Length > ErrorMaxLength ? error[..ErrorMaxLength] : error;
    }

    public void RecordVerification(DateTime utcNow) => VerifiedAtUtc = utcNow;

    private static void Require(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException(message);
        }
    }
}
