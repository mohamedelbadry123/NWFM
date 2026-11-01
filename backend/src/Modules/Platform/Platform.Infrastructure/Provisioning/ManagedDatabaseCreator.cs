using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NWFM.Shared.Options;
using Tenancy.Infrastructure.Persistence;

namespace Platform.Infrastructure.Provisioning;

/// <summary>
/// Creates a tenant database on the pooled server with the most room, plus a principal that can reach
/// that database and nothing else. Re-runnable: every statement checks before it creates, and an
/// existing principal just gets the new password.
/// </summary>
internal sealed class ManagedDatabaseCreator(
    CatalogDbContext catalog,
    ISecretProtector secretProtector,
    IOptions<PlatformOptions> platformOptions)
{
    private const string MasterDatabase = "master";
    private const string OwnerSuffix = "_owner";
    private const string RuntimeSuffix = "_app";
    private const int PasswordLength = 32;
    private const int CreateDatabaseTimeoutSeconds = 300;
    private const int StatementTimeoutSeconds = 60;
    private const string PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    private const string CreateDatabaseSql = """
        IF DB_ID(@db) IS NULL
        BEGIN
            DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@db);
            EXEC (@sql);
        END
        """;

    private const string EnableContainmentSql = """
        IF EXISTS (SELECT 1 FROM sys.databases WHERE name = @db AND containment = 0)
        BEGIN
            DECLARE @sql nvarchar(max) = N'ALTER DATABASE ' + QUOTENAME(@db) + N' SET CONTAINMENT = PARTIAL';
            EXEC (@sql);
        END
        """;

    private const string ContainedUserSql = """
        DECLARE @sql nvarchar(max);
        IF DATABASE_PRINCIPAL_ID(@principal) IS NULL
            SET @sql = N'CREATE USER ' + QUOTENAME(@principal) + N' WITH PASSWORD = ' + QUOTENAME(@pwd, '''');
        ELSE
            SET @sql = N'ALTER USER ' + QUOTENAME(@principal) + N' WITH PASSWORD = ' + QUOTENAME(@pwd, '''');
        EXEC (@sql);
        """;

    private const string ServerLoginSql = """
        DECLARE @sql nvarchar(max);
        IF SUSER_ID(@principal) IS NULL
            SET @sql = N'CREATE LOGIN ' + QUOTENAME(@principal) + N' WITH PASSWORD = ' + QUOTENAME(@pwd, '''') + N', CHECK_POLICY = ON';
        ELSE
            SET @sql = N'ALTER LOGIN ' + QUOTENAME(@principal) + N' WITH PASSWORD = ' + QUOTENAME(@pwd, '''');
        EXEC (@sql);
        """;

    private const string UserForLoginSql = """
        IF DATABASE_PRINCIPAL_ID(@principal) IS NULL
        BEGIN
            DECLARE @sql nvarchar(max) = N'CREATE USER ' + QUOTENAME(@principal) + N' FOR LOGIN ' + QUOTENAME(@principal);
            EXEC (@sql);
        END
        """;

    // db_owner of its own database only: migrations need DDL, and the owner reaches nothing else.
    private const string GrantOwnerSql = """
        IF IS_ROLEMEMBER('db_owner', @principal) = 0
        BEGIN
            DECLARE @sql nvarchar(max) = N'ALTER ROLE db_owner ADD MEMBER ' + QUOTENAME(@principal);
            EXEC (@sql);
        END
        """;

    public static string DatabaseNameFor(string prefix, string tenantKey) => prefix + tenantKey.Replace('-', '_');

    /// <summary>
    /// Two principals per database: the owner the control plane migrates with (<c>db_owner</c> of this
    /// database only) and the runtime principal requests run as, which gets no role here; the runtime
    /// grants step of the migration gives it exactly what request handling needs.
    /// </summary>
    public async Task<TenantDatabase> CreateAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        var server = await PickServerAsync(cancellationToken)
            ?? throw new InvalidOperationException("No active database server has room for another database.");

        var adminConnectionString = secretProtector.Unprotect(SecretPurposes.DatabaseServerAdmin, server.EncryptedAdminConnectionString);
        var databaseName = DatabaseNameFor(platformOptions.Value.ManagedDatabaseNamePrefix, tenant.Key);
        var owner = databaseName + OwnerSuffix;
        var runtime = databaseName + RuntimeSuffix;
        var ownerPassword = GeneratePassword();
        var runtimePassword = GeneratePassword();

        var master = WithCatalog(adminConnectionString, MasterDatabase);
        var tenantAsAdmin = WithCatalog(adminConnectionString, databaseName);

        await ExecuteAsync(master, CreateDatabaseSql, CreateDatabaseTimeoutSeconds, cancellationToken, ("@db", databaseName));

        if (server.UseContainedUsers)
        {
            await ExecuteAsync(master, EnableContainmentSql, StatementTimeoutSeconds, cancellationToken, ("@db", databaseName));
        }

        foreach (var (principal, password) in new[] { (owner, ownerPassword), (runtime, runtimePassword) })
        {
            if (server.UseContainedUsers)
            {
                await ExecuteAsync(tenantAsAdmin, ContainedUserSql, StatementTimeoutSeconds, cancellationToken, ("@principal", principal), ("@pwd", password));
            }
            else
            {
                await ExecuteAsync(master, ServerLoginSql, StatementTimeoutSeconds, cancellationToken, ("@principal", principal), ("@pwd", password));
                await ExecuteAsync(tenantAsAdmin, UserForLoginSql, StatementTimeoutSeconds, cancellationToken, ("@principal", principal));
            }
        }

        await ExecuteAsync(tenantAsAdmin, GrantOwnerSql, StatementTimeoutSeconds, cancellationToken, ("@principal", owner));

        return TenantDatabase.CreateManaged(
            tenant.Id,
            server.Id,
            databaseName,
            owner,
            runtime,
            secretProtector.Protect(SecretPurposes.TenantRuntimeConnection,
                BuildTenantConnectionString(adminConnectionString, databaseName, runtime, runtimePassword)),
            secretProtector.Protect(SecretPurposes.TenantAdminConnection,
                BuildTenantConnectionString(adminConnectionString, databaseName, owner, ownerPassword)));
    }

    private async Task<DatabaseServer?> PickServerAsync(CancellationToken cancellationToken)
    {
        var candidates = await catalog.DatabaseServers
            .AsNoTracking()
            .Where(s => s.IsActive)
            .Select(s => new { Server = s, Used = catalog.TenantDatabases.Count(d => d.DatabaseServerId == s.Id) })
            .ToListAsync(cancellationToken);

        return candidates
            .Where(c => c.Used < c.Server.MaxDatabases)
            .OrderBy(c => (double)c.Used / c.Server.MaxDatabases)
            .Select(c => c.Server)
            .FirstOrDefault();
    }

    private static string WithCatalog(string connectionString, string database) =>
        new SqlConnectionStringBuilder(connectionString) { InitialCatalog = database }.ConnectionString;

    private static string BuildTenantConnectionString(string adminConnectionString, string database, string principal, string password)
    {
        var builder = new SqlConnectionStringBuilder(adminConnectionString)
        {
            InitialCatalog = database,
            IntegratedSecurity = false,
            UserID = principal,
            Password = password,
            PersistSecurityInfo = false
        };

        if (builder.Authentication != SqlAuthenticationMethod.NotSpecified)
        {
            builder.Authentication = SqlAuthenticationMethod.SqlPassword;
        }

        return builder.ConnectionString;
    }

    private static async Task ExecuteAsync(string connectionString, string sql, int timeoutSeconds, CancellationToken cancellationToken,
        params (string Name, string Value)[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = timeoutSeconds;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.Add(new SqlParameter(name, System.Data.SqlDbType.NVarChar, 128) { Value = value });
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string GeneratePassword()
    {
        while (true)
        {
            var candidate = RandomNumberGenerator.GetString(PasswordAlphabet, PasswordLength);
            if (candidate.Any(char.IsUpper) && candidate.Any(char.IsLower) && candidate.Any(char.IsDigit))
            {
                return candidate;
            }
        }
    }
}
