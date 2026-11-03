using System.Data;
using Microsoft.Data.SqlClient;

namespace Auth.Infrastructure.Operations;

/// <summary>
/// Offline platform operations. Never exposed through a tenant HTTP endpoint.
/// Authorization comes from the authenticated SQL identity's explicit operator
/// role, and the database binding constrains that capability to one tenant.
/// </summary>
public sealed class TenantLifecycleOperations(string connectionString, TimeProvider timeProvider,
    TenantRetentionOptions? retentionOptions = null)
{
    public async Task<TenantRetentionSchedule> OffboardAsync(Guid tenantId, string reason, bool legalHold,
        CancellationToken ct = default)
    {
        TenantRetentionPolicy.RequireReason(reason);
        var schedule = TenantRetentionPolicy.Schedule(tenantId, timeProvider.GetUtcNow(), legalHold, retentionOptions);
        await using var connection = await OpenOperatorConnectionAsync(connectionString, tenantId, ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            IF NOT EXISTS (SELECT 1 FROM Tenancy.DeploymentBoundary WITH (UPDLOCK,HOLDLOCK)
                WHERE SingletonId=1 AND TenantId=@tenant AND IsActive=1)
                THROW 51101, 'Tenant is already inactive or its ownership changed.', 1;
            UPDATE Tenancy.DeploymentBoundary SET IsActive=0 WHERE SingletonId=1 AND TenantId=@tenant;
            UPDATE Auth.RefreshTokens SET RevokedAtUtc=@now WHERE RevokedAtUtc IS NULL;
            UPDATE Auth.SsoAuthorizationCodes SET ConsumedAtUtc=@now WHERE ConsumedAtUtc IS NULL;
            UPDATE Auth.Users SET SecurityStamp=CONVERT(nvarchar(36),NEWID()), LockoutEnabled=1,
                LockoutEnd=CONVERT(datetimeoffset,'9999-12-31T23:59:59+00:00');
            INSERT Tenancy.LifecycleAudit
                (Id,TenantId,Action,OperatorLogin,Reason,OccurredAtUtc,ActiveDataDueUtc,BackupDueUtc,AuditDueUtc,LegalHold)
            VALUES (NEWID(),@tenant,N'Offboarded',ORIGINAL_LOGIN(),@reason,@now,@activeDue,@backupDue,@auditDue,@hold);
            """;
        command.Parameters.Add("@tenant", SqlDbType.UniqueIdentifier).Value = tenantId;
        command.Parameters.Add("@reason", SqlDbType.NVarChar, 1024).Value = reason.Trim();
        command.Parameters.Add("@now", SqlDbType.DateTime2).Value = schedule.OffboardedAt.UtcDateTime;
        command.Parameters.Add("@activeDue", SqlDbType.DateTime2).Value = schedule.ActiveDataDue.UtcDateTime;
        command.Parameters.Add("@backupDue", SqlDbType.DateTime2).Value = schedule.BackupsDue.UtcDateTime;
        command.Parameters.Add("@auditDue", SqlDbType.DateTime2).Value = schedule.AuditDue.UtcDateTime;
        command.Parameters.Add("@hold", SqlDbType.Bit).Value = legalHold;
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return schedule;
    }

    public async Task<TenantRetentionSchedule> RetentionAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var connection = await OpenOperatorConnectionAsync(connectionString, tenantId, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP(1) OccurredAtUtc,ActiveDataDueUtc,BackupDueUtc,AuditDueUtc,LegalHold
            FROM Tenancy.LifecycleAudit WHERE TenantId=@tenant AND Action=N'Offboarded'
            ORDER BY OccurredAtUtc DESC;
            """;
        command.Parameters.Add("@tenant", SqlDbType.UniqueIdentifier).Value = tenantId;
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException("No offboarding retention schedule exists for this tenant.");
        static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
        return new(tenantId, Utc(reader.GetDateTime(0)), Utc(reader.GetDateTime(1)),
            Utc(reader.GetDateTime(2)), Utc(reader.GetDateTime(3)), reader.GetBoolean(4));
    }

    public async Task VerifyRestoreAsync(Guid tenantId, string recoveryConnectionString,
        string recoveryStorageRoot, string sourceBackup, string reason, CancellationToken ct = default)
    {
        TenantRetentionPolicy.RequireReason(reason);
        if (string.IsNullOrWhiteSpace(sourceBackup) || sourceBackup.Length > 512)
            throw new ArgumentException("A source backup reference of at most 512 characters is required.", nameof(sourceBackup));
        // Authorize against the current target before reading a recovery resource.
        await using var target = await OpenOperatorConnectionAsync(connectionString, tenantId, ct);
        try
        {
            await using var recovery = await OpenOperatorConnectionAsync(recoveryConnectionString, tenantId, ct);
            if (string.Equals(target.DataSource, recovery.DataSource, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(target.Database, recovery.Database, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A live tenant database cannot be its recovery source.");
            await using (var state = recovery.CreateCommand())
            {
                state.CommandText = """
                    SELECT CONVERT(bit,CASE WHEN DATABASEPROPERTYEX(DB_NAME(),'Updateability')='READ_ONLY' THEN 1 ELSE 0 END),
                           CONVERT(bit,CASE WHEN DATABASEPROPERTYEX(DB_NAME(),'UserAccess')='SINGLE_USER' THEN 1 ELSE 0 END);
                    """;
                await using var reader = await state.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Recovery state is unavailable.");
                TenantRetentionPolicy.RequireRecoveryDatabase(recovery.Database, reader.GetBoolean(0), reader.GetBoolean(1));
            }
            await VerifyOwnedRowsAsync(recovery, tenantId, ct);
            var root = Path.GetFullPath(recoveryStorageRoot);
            var marker = Path.Combine(root, ".nwfm-tenant");
            if (!File.Exists(marker) ||
                (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Recovery storage must have a local, unredirected tenant ownership marker.");
            for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Recovery storage cannot traverse a redirected directory.");
            TenantRetentionPolicy.RequireStorageOwner(tenantId, await File.ReadAllTextAsync(marker, ct));

            await AuditRestoreAsync(target, tenantId, "RestoreOwnershipVerified", reason, sourceBackup, recovery.Database, ct);
        }
        catch
        {
            // The target is already authorized. Record a generic denial outcome
            // without leaking source contents, connection strings or SQL errors.
            await AuditRestoreAsync(target, tenantId, "RestoreOwnershipDenied", reason, sourceBackup, null, CancellationToken.None);
            throw;
        }
        // This is ownership verification, never permission to restore old users,
        // sessions, policies or expired data. A separately reviewed data-only copy
        // must apply the current target's access and retention rules.
    }

    private static async Task AuditRestoreAsync(SqlConnection target, Guid tenantId, string action,
        string reason, string sourceBackup, string? recoveryDatabase, CancellationToken ct)
    {
        await using var audit = target.CreateCommand();
        audit.CommandText = """
            INSERT Tenancy.LifecycleAudit
                (Id,TenantId,Action,OperatorLogin,Reason,OccurredAtUtc,LegalHold,SourceBackup,RecoveryDatabase)
            VALUES (NEWID(),@tenant,@action,ORIGINAL_LOGIN(),@reason,SYSUTCDATETIME(),0,@source,@database);
            """;
        audit.Parameters.Add("@tenant", SqlDbType.UniqueIdentifier).Value = tenantId;
        audit.Parameters.Add("@action", SqlDbType.NVarChar, 64).Value = action;
        audit.Parameters.Add("@reason", SqlDbType.NVarChar, 1024).Value = reason.Trim();
        audit.Parameters.Add("@source", SqlDbType.NVarChar, 512).Value = sourceBackup;
        audit.Parameters.Add("@database", SqlDbType.NVarChar, 128).Value = (object?)recoveryDatabase ?? DBNull.Value;
        await audit.ExecuteNonQueryAsync(ct);
    }

    public static async Task VerifyOwnedRowsAsync(SqlConnection connection, Guid tenantId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT QUOTENAME(s.name)+N'.'+QUOTENAME(t.name),QUOTENAME(c.name)
            FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
            JOIN sys.columns c ON c.object_id=t.object_id
            WHERE c.system_type_id=36 AND c.name IN(N'OrganizationId',N'TenantId');
            """;
        var columns = new List<(string Table, string Column)>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) columns.Add((reader.GetString(0), reader.GetString(1)));
        command.Parameters.Add("@tenant", SqlDbType.UniqueIdentifier).Value = tenantId;
        foreach (var (table, column) in columns)
        {
            // Identifiers originate only from SQL metadata and QUOTENAME.
            // These two nullable rows are defaults local to this dedicated
            // deployment database; no other unowned tenant rows are allowed.
            var nullableDefault = column == "[OrganizationId]" &&
                table is "[Workflow].[BusinessCalendars]" or "[Workflow].[SlaPolicies]";
            var missingOwner = nullableDefault ? "" : $"{column} IS NULL OR ";
            command.CommandText = $"IF EXISTS (SELECT 1 FROM {table} WHERE {missingOwner}{column}<>@tenant) THROW 51102, 'Recovery contains unowned or foreign-tenant rows.', 1;";
            await command.ExecuteNonQueryAsync(ct);
        }
        command.CommandText = """
            IF OBJECT_ID(N'Tenancy.Tenants',N'U') IS NOT NULL
                EXEC sp_executesql N'IF EXISTS(SELECT 1 FROM Tenancy.Tenants WHERE Id<>@id)
                    THROW 51102,''Recovery contains foreign tenants.'',1;',N'@id uniqueidentifier',@id=@tenant;
            """;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<SqlConnection> OpenOperatorConnectionAsync(string connectionString, Guid tenantId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant ID is required.", nameof(tenantId));
        var settings = new SqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(settings.InitialCatalog) ||
            new[] { "master", "model", "msdb", "tempdb" }.Contains(settings.InitialCatalog, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("A dedicated tenant database is required.");
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct);
            await using var authorize = connection.CreateCommand();
            authorize.CommandText = """
                IF COALESCE(IS_MEMBER(N'nwfm_tenant_operator'),0)<>1
                    THROW 51103, 'An explicitly authorized tenant operator is required.', 1;
                IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'VIEW DEFINITION'),0)<>1
                    THROW 51105, 'Complete schema visibility is required for tenant ownership verification.', 1;
                IF NOT EXISTS(SELECT 1 FROM Tenancy.DeploymentBoundary WHERE SingletonId=1 AND TenantId=@tenant)
                    THROW 51104, 'Operator target tenant does not match database ownership.', 1;
                """;
            authorize.Parameters.Add("@tenant", SqlDbType.UniqueIdentifier).Value = tenantId;
            await authorize.ExecuteNonQueryAsync(ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
