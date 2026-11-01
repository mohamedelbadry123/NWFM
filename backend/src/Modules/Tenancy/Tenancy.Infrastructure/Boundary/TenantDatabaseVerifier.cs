using System.Collections.Concurrent;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NWFM.Shared.Options;

namespace Tenancy.Infrastructure.Boundary;

/// <summary>
/// Proves a tenant's runtime credential is confined to its own database before any request or worker
/// uses it: no server or database administration, no access to another application database, no
/// impersonation, a matching and read-only boundary binding, and the form-storage ownership the
/// dynamic tables rely on. Run by the control plane at provisioning and on demand, and by the tenant
/// API lazily per tenant (a pass is trusted for <see cref="TenancyOptions.VerificationMinutes"/>).
/// </summary>
public sealed class TenantDatabaseVerifier(
    TimeProvider clock,
    IOptions<TenancyOptions> options,
    ILogger<TenantDatabaseVerifier> logger)
{
    private static readonly TimeSpan FailureRetry = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<string, (bool Passed, DateTimeOffset Until)> _results = new(StringComparer.Ordinal);

    /// <summary>Cached result for <paramref name="tenantId"/> over <paramref name="runtimeConnectionString"/>; fails closed.</summary>
    public async ValueTask<bool> IsVerifiedAsync(Guid tenantId, string runtimeConnectionString, CancellationToken cancellationToken)
    {
        var key = tenantId.ToString("N") + ":" + Hash(runtimeConnectionString);
        var now = clock.GetUtcNow();
        if (_results.TryGetValue(key, out var cached) && cached.Until > now)
            return cached.Passed;

        bool passed;
        try
        {
            await VerifyRuntimeAsync(runtimeConnectionString, tenantId, cancellationToken);
            passed = true;
        }
        catch (Exception error) when (error is SqlException or InvalidOperationException)
        {
            logger.LogError(new EventId(4204, "TenantDatabaseUnverified"),
                "Tenant {TenantId} failed database isolation verification: {Reason}", tenantId, error.Message);
            passed = false;
        }
        _results[key] = (passed, now + (passed ? TimeSpan.FromMinutes(options.Value.VerificationMinutes) : FailureRetry));
        return passed;
    }

    /// <summary>Throws with the failed check when the runtime credential is not confined to the tenant's database.</summary>
    public static async Task VerifyRuntimeAsync(string runtimeConnectionString, Guid tenantId, CancellationToken cancellationToken)
    {
        var settings = RequireApplicationDatabase(runtimeConnectionString, allowRecovery: false);
        await using var connection = new SqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);
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
        command.Parameters.Add("@tenant", SqlDbType.UniqueIdentifier).Value = tenantId;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Connection-string rules every tenant database must meet before it is opened at all.</summary>
    public static SqlConnectionStringBuilder RequireApplicationDatabase(string connectionString, bool allowRecovery)
    {
        var settings = new SqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(settings.InitialCatalog) ||
            new[] { "master", "model", "msdb", "tempdb" }.Contains(settings.InitialCatalog, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("A dedicated application database is required.");
        if (settings.MaxPoolSize > 100)
            throw new InvalidOperationException("The tenant database connection pool must not exceed 100 connections.");
        if (!allowRecovery && settings.InitialCatalog.StartsWith("NWFM_RECOVERY_", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Recovery databases cannot serve application requests.");
        return settings;
    }

    /// <summary>A principal name the grant step may quote: letters, digits, underscore and hyphen only.</summary>
    public static bool IsSafePrincipalName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= 128 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

/// <summary>
/// The tenant API's readiness gates: a database whose recorded schema is behind this build is in an
/// upgrade window (503), and a runtime credential that fails isolation verification is never used.
/// </summary>
internal sealed class TenantRuntimeReadiness(
    IServiceProvider services,
    ITenantConnectionStringResolver connections,
    TenantDatabaseVerifier verifier) : ITenantReadiness
{
    public async ValueTask<TenantAccessException?> CheckAsync(TenantInfo tenant, CancellationToken cancellationToken)
    {
        var modules = services.GetServices<ITenantDatabaseModule>();
        if (TenantSchema.IsBehind(tenant.SchemaVersion, modules))
            return TenantAccessException.SchemaUpgradePending();
        return await verifier.IsVerifiedAsync(tenant.Id, connections.Resolve(tenant), cancellationToken)
            ? null
            : TenantAccessException.Unverified();
    }
}
