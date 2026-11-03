using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Tenancy.Infrastructure.Boundary;

/// <summary>
/// First step of every tenant database migration (Order 0, before any module's EF migrations): binds
/// the database to its catalog tenant and prepares the objects the isolation checks rely on. Run by
/// the control plane with the owner credential.
/// </summary>
/// <remarks>
/// Creates the <c>Tenancy</c> schema, the singleton <c>Tenancy.DeploymentBoundary</c> row (refusing a
/// database already bound to another tenant, which also stops a bring-your-own or restored database
/// being adopted by the wrong workspace), the <c>Tenancy.LifecycleAudit</c> ledger, and the no-login
/// <c>nwfm_form_storage_owner</c> that owns the <c>FormEngine</c> schema. Rows already carrying another
/// tenant's <c>OrganizationId</c> make the database unusable.
/// </remarks>
public sealed class TenantBoundaryModule : ITenantDatabaseModule
{
    /// <summary>Versioned so a change to this DDL is part of the schema gate like any EF migration.</summary>
    public const string Version = "20261101000000_Boundary_v1";

    public string Name => "Boundary";

    public int Order => 0;

    public string? LatestMigrationId => Version;

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var tenant = services.GetRequiredService<ITenantContext>().Current ?? throw TenantAccessException.Required();
        var connectionString = services.GetRequiredService<ITenantConnectionStringResolver>().Resolve(tenant);
        await BindAsync(connectionString, tenant.Id, cancellationToken);
    }

    public Task SeedAsync(IServiceProvider services, TenantSeedProfile profile, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public async Task<IReadOnlyList<string>> GetPendingMigrationsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var tenant = services.GetRequiredService<ITenantContext>().Current ?? throw TenantAccessException.Required();
        await using var connection = new SqlConnection(services.GetRequiredService<ITenantConnectionStringResolver>().Resolve(tenant));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(N'Tenancy.DeploymentBoundary', N'U') IS NULL THEN 0 ELSE 1 END";
        return await command.ExecuteScalarAsync(cancellationToken) is 1 ? [] : [Version];
    }

    public static async Task BindAsync(string connectionString, Guid tenantId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
            throw new InvalidOperationException("A non-empty tenant is required.");
        var settings = TenantDatabaseVerifier.RequireApplicationDatabase(connectionString, allowRecovery: true);

        await using var connection = new SqlConnection(settings.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        catch (SqlException error) when (error.Number == 4060)
        {
            // Configuration-mode tenants may name a database that does not exist yet (fresh dev box).
            var master = new SqlConnectionStringBuilder(settings.ConnectionString) { InitialCatalog = "master" };
            await using var administrative = new SqlConnection(master.ConnectionString);
            await administrative.OpenAsync(cancellationToken);
            await using var create = administrative.CreateCommand();
            create.CommandText = "CREATE DATABASE " + Quote(settings.InitialCatalog);
            await create.ExecuteNonQueryAsync(cancellationToken);
            // SqlClient caches failed logins for the pool's blocking period.
            SqlConnection.ClearPool(connection);
            await connection.OpenAsync(cancellationToken);
        }

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DECLARE @locked int;
            EXEC @locked = sys.sp_getapplock @Resource=N'NWFM.DeploymentTenant',
                @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
            IF @locked < 0 THROW 51000, 'Could not lock the tenant binding.', 1;
            IF SCHEMA_ID(N'Tenancy') IS NULL EXEC(N'CREATE SCHEMA [Tenancy]');
            IF OBJECT_ID(N'Tenancy.DeploymentBoundary', N'U') IS NULL
                CREATE TABLE Tenancy.DeploymentBoundary (
                    SingletonId int NOT NULL CONSTRAINT PK_DeploymentBoundary PRIMARY KEY
                        CONSTRAINT CK_DeploymentBoundary_Singleton CHECK (SingletonId=1),
                    TenantId uniqueidentifier NOT NULL,
                    IsActive bit NOT NULL CONSTRAINT DF_DeploymentBoundary_IsActive DEFAULT 1,
                    CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
                );
            IF COL_LENGTH(N'Tenancy.DeploymentBoundary',N'IsActive') IS NULL
                ALTER TABLE Tenancy.DeploymentBoundary ADD IsActive bit NOT NULL
                    CONSTRAINT DF_DeploymentBoundary_IsActive DEFAULT 1;
            IF OBJECT_ID(N'Tenancy.LifecycleAudit',N'U') IS NULL
                CREATE TABLE Tenancy.LifecycleAudit (
                    Id uniqueidentifier NOT NULL CONSTRAINT PK_TenantLifecycleAudit PRIMARY KEY,
                    TenantId uniqueidentifier NOT NULL,
                    Action nvarchar(64) NOT NULL,
                    OperatorLogin nvarchar(256) NOT NULL,
                    Reason nvarchar(1024) NOT NULL,
                    OccurredAtUtc datetime2 NOT NULL,
                    ActiveDataDueUtc datetime2 NULL,
                    BackupDueUtc datetime2 NULL,
                    AuditDueUtc datetime2 NULL,
                    LegalHold bit NOT NULL,
                    SourceBackup nvarchar(512) NULL,
                    RecoveryDatabase nvarchar(128) NULL
                );
            IF EXISTS (SELECT 1 FROM Tenancy.DeploymentBoundary WHERE TenantId <> @tenant)
                THROW 51001, 'Database belongs to another tenant.', 1;
            IF USER_ID(N'nwfm_form_storage_owner') IS NULL
                CREATE USER [nwfm_form_storage_owner] WITHOUT LOGIN;
            IF NOT EXISTS (SELECT 1 FROM sys.database_principals
                WHERE name=N'nwfm_form_storage_owner' AND type=N'S' AND authentication_type=0)
                THROW 51012, 'Form storage requires its dedicated non-login schema owner.', 1;
            IF EXISTS (SELECT 1 FROM sys.database_role_members
                WHERE member_principal_id=USER_ID(N'nwfm_form_storage_owner'))
                OR EXISTS (SELECT 1 FROM sys.database_permissions
                    WHERE grantee_principal_id=USER_ID(N'nwfm_form_storage_owner')
                        AND NOT (class=0 AND permission_name=N'CONNECT'))
                OR EXISTS (SELECT 1 FROM sys.schemas
                    WHERE principal_id=USER_ID(N'nwfm_form_storage_owner') AND name<>N'FormEngine')
                THROW 51013, 'The form storage owner cannot have other database capabilities.', 1;
            IF SCHEMA_ID(N'FormEngine') IS NULL
                EXEC(N'CREATE SCHEMA [FormEngine] AUTHORIZATION [nwfm_form_storage_owner]');
            ELSE IF EXISTS (SELECT 1 FROM sys.schemas
                WHERE name=N'FormEngine' AND principal_id<>USER_ID(N'nwfm_form_storage_owner'))
                ALTER AUTHORIZATION ON SCHEMA::[FormEngine] TO [nwfm_form_storage_owner];
            IF EXISTS (SELECT 1 FROM sys.objects
                WHERE schema_id=SCHEMA_ID(N'FormEngine') AND principal_id IS NOT NULL
                    AND principal_id<>USER_ID(N'nwfm_form_storage_owner'))
                THROW 51014, 'Form storage contains objects with a different explicit owner.', 1;
            """;
        command.Parameters.Add("@tenant", SqlDbType.UniqueIdentifier).Value = tenantId;
        await command.ExecuteNonQueryAsync(cancellationToken);

        // Discover existing row-owned tables; do not adopt a contaminated database.
        command.CommandText = """
            SELECT QUOTENAME(s.name)+N'.'+QUOTENAME(t.name)
            FROM sys.tables t JOIN sys.schemas s ON t.schema_id=s.schema_id
            JOIN sys.columns c ON c.object_id=t.object_id
            WHERE c.name=N'OrganizationId' AND c.system_type_id=36;
            """;
        var tables = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) tables.Add(reader.GetString(0));
        foreach (var table in tables)
        {
            // These classified defaults are shared only inside this one tenant's database.
            var sharedDefault = table is "[Workflow].[BusinessCalendars]" or "[Workflow].[SlaPolicies]";
            var invalidOwner = sharedDefault ? "OrganizationId <> @tenant" : "OrganizationId IS NULL OR OrganizationId <> @tenant";
            command.CommandText = $"IF EXISTS (SELECT 1 FROM {table} WHERE {invalidOwner}) THROW 51002, 'Database contains another tenant or unowned records.', 1;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        command.CommandText = """
            IF OBJECT_ID(N'Tenancy.Tenants', N'U') IS NOT NULL
                EXEC sp_executesql N'IF EXISTS (SELECT 1 FROM Tenancy.Tenants WHERE Id<>@id)
                    THROW 51003, ''Database contains another tenant.'', 1;',
                    N'@id uniqueidentifier', @id=@tenant;
            IF NOT EXISTS (SELECT 1 FROM Tenancy.DeploymentBoundary)
            BEGIN
                INSERT Tenancy.DeploymentBoundary (SingletonId,TenantId) VALUES (1,@tenant);
                INSERT Tenancy.LifecycleAudit
                    (Id,TenantId,Action,OperatorLogin,Reason,OccurredAtUtc,LegalHold)
                VALUES (NEWID(),@tenant,N'Provisioned',ORIGINAL_LOGIN(),
                    N'Tenant database bound by the control plane.',SYSUTCDATETIME(),0);
            END;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static string Quote(string identifier) => "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";
}
