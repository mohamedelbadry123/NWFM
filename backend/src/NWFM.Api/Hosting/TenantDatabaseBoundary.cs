using System.Data;
using Microsoft.Data.SqlClient;
using NWFM.Shared.Options;

namespace NWFM.Api.Hosting;

/// <summary>
/// Auth, forms and tasks use database isolation. Bind the entire database before
/// migrations or requests; a configuration change must never reassign its owner.
/// </summary>
public static class TenantDatabaseBoundary
{
    public static void ValidateStartup(DatabaseStartupOptions startup)
    {
        if (!startup.InitializeOnly && (startup.ApplyMigrations || startup.SeedData || startup.ApplySqlObjects))
            throw new InvalidOperationException("Database initialization requires DatabaseStartup:InitializeOnly=true. Run it separately from the request host.");
    }

    public static async Task VerifyAsync(string connectionString, Guid tenantId, bool initializeOnly,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new InvalidOperationException("A non-empty deployment tenant is required.");
        var settings = new SqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(settings.InitialCatalog) ||
            new[] { "master", "model", "msdb", "tempdb" }.Contains(settings.InitialCatalog, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("A dedicated application database is required.");
        if (settings.MaxPoolSize > 100)
            throw new InvalidOperationException("The tenant database connection pool must not exceed 100 connections.");
        if (!initializeOnly && settings.InitialCatalog.StartsWith("NWFM_RECOVERY_", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Recovery databases cannot serve application requests.");

        await using var connection = new SqlConnection(settings.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        catch (SqlException error) when (initializeOnly && error.Number == 4060)
        {
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

        if (initializeOnly)
        {
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                DECLARE @locked int;
                EXEC @locked = sys.sp_getapplock @Resource=N'NWFM.DeploymentTenant',
                    @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
                IF @locked < 0 THROW 51000, 'Could not lock deployment tenant binding.', 1;
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
                        N'Dedicated deployment initialized.',SYSUTCDATETIME(),0);
                END;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                IF COALESCE(IS_SRVROLEMEMBER(N'sysadmin'),1)<>0
                    OR COALESCE(IS_SRVROLEMEMBER(N'serveradmin'),1)<>0
                    OR COALESCE(IS_SRVROLEMEMBER(N'securityadmin'),1)<>0
                    OR COALESCE(IS_SRVROLEMEMBER(N'setupadmin'),1)<>0
                    OR COALESCE(IS_SRVROLEMEMBER(N'processadmin'),1)<>0
                    OR COALESCE(IS_SRVROLEMEMBER(N'diskadmin'),1)<>0
                    OR COALESCE(IS_SRVROLEMEMBER(N'dbcreator'),1)<>0
                    OR COALESCE(IS_SRVROLEMEMBER(N'bulkadmin'),1)<>0
                    OR COALESCE(IS_MEMBER(N'db_owner'),1)<>0
                    OR COALESCE(IS_MEMBER(N'db_securityadmin'),1)<>0
                    OR COALESCE(IS_MEMBER(N'db_ddladmin'),1)<>0
                    OR COALESCE(HAS_PERMS_BY_NAME(NULL,NULL,N'CONTROL SERVER'),1)<>0
                    OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CONTROL'),1)<>0
                    OR COALESCE(HAS_PERMS_BY_NAME(NULL,NULL,N'ALTER ANY LOGIN'),1)<>0
                    OR COALESCE(HAS_PERMS_BY_NAME(NULL,NULL,N'IMPERSONATE ANY LOGIN'),1)<>0
                    OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'ALTER ANY USER'),1)<>0
                    OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'ALTER ANY ROLE'),1)<>0
                    THROW 51004, 'The request database identity has administrative privileges.', 1;
                IF COALESCE(HAS_PERMS_BY_NAME(NULL,NULL,N'VIEW ANY DATABASE'),0)<>1
                    THROW 51010, 'The request identity cannot verify the database isolation inventory.', 1;
                IF EXISTS (SELECT 1 FROM sys.databases
                    WHERE database_id>4 AND name<>DB_NAME() AND HAS_DBACCESS(name)=1)
                    THROW 51007, 'The request identity can access another application database.', 1;
                IF EXISTS (SELECT 1 FROM sys.database_principals
                    WHERE name<>USER_NAME() AND type IN ('S','U','G')
                    AND HAS_PERMS_BY_NAME(name,N'USER',N'IMPERSONATE')=1)
                    THROW 51008, 'The request identity can impersonate another database user.', 1;
                IF EXISTS (SELECT 1 FROM sys.server_principals
                    WHERE name<>SUSER_SNAME() AND type IN ('S','U','G')
                    AND HAS_PERMS_BY_NAME(name,N'LOGIN',N'IMPERSONATE')=1)
                    THROW 51011, 'The request identity can impersonate another server login.', 1;
                IF NOT EXISTS (SELECT 1 FROM Tenancy.DeploymentBoundary WHERE SingletonId=1 AND TenantId=@tenant)
                    THROW 51005, 'Database tenant binding is missing or mismatched.', 1;
                IF COALESCE(HAS_PERMS_BY_NAME(N'Tenancy.DeploymentBoundary',N'OBJECT',N'UPDATE'),1)<>0
                    OR COALESCE(HAS_PERMS_BY_NAME(N'Tenancy.DeploymentBoundary',N'OBJECT',N'INSERT'),1)<>0
                    OR COALESCE(HAS_PERMS_BY_NAME(N'Tenancy.DeploymentBoundary',N'OBJECT',N'DELETE'),1)<>0
                    OR COALESCE(HAS_PERMS_BY_NAME(N'Tenancy.DeploymentBoundary',N'OBJECT',N'ALTER'),1)<>0
                    THROW 51006, 'The request identity can modify its tenant binding.', 1;
                IF COALESCE(HAS_PERMS_BY_NAME(N'Tenancy.LifecycleAudit',N'OBJECT',N'INSERT'),1)<>0
                    OR COALESCE(HAS_PERMS_BY_NAME(N'Tenancy.LifecycleAudit',N'OBJECT',N'UPDATE'),1)<>0
                    OR COALESCE(HAS_PERMS_BY_NAME(N'Tenancy.LifecycleAudit',N'OBJECT',N'DELETE'),1)<>0
                    OR COALESCE(HAS_PERMS_BY_NAME(N'Tenancy.LifecycleAudit',N'OBJECT',N'ALTER'),1)<>0
                    THROW 51009, 'The request identity can modify the lifecycle audit ledger.', 1;
                """;
            command.Parameters.Add("@tenant", SqlDbType.UniqueIdentifier).Value = tenantId;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await VerifyFormSchemaOwnershipAsync(connection, cancellationToken);
    }

    private static async Task VerifyFormSchemaOwnershipAsync(SqlConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @formOwner int=USER_ID(N'nwfm_form_storage_owner');
            IF @formOwner IS NULL OR NOT EXISTS (
                SELECT 1 FROM sys.database_principals p JOIN sys.schemas s ON s.principal_id=p.principal_id
                WHERE p.principal_id=@formOwner AND p.type=N'S' AND p.authentication_type=0 AND s.name=N'FormEngine')
                THROW 51012, 'Form storage requires its dedicated non-login schema owner.', 1;
            IF EXISTS (SELECT 1 FROM sys.schemas WHERE name<>N'FormEngine' AND principal_id=@formOwner)
                OR EXISTS (SELECT 1 FROM sys.objects o JOIN sys.schemas s ON s.schema_id=o.schema_id
                    WHERE s.name<>N'FormEngine' AND COALESCE(o.principal_id,s.principal_id)=@formOwner)
                THROW 51013, 'Form storage must not share ownership with protected database objects.', 1;
            IF EXISTS (SELECT 1 FROM sys.objects
                WHERE schema_id=SCHEMA_ID(N'FormEngine') AND principal_id IS NOT NULL AND principal_id<>@formOwner)
                THROW 51014, 'Form storage contains objects with a different explicit owner.', 1;
            """;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string Quote(string identifier) => "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";
}
