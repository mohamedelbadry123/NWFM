using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tenancy.Infrastructure.MultiTenancy;

namespace Tenancy.Infrastructure.Boundary;

/// <summary>
/// Last step of every tenant database migration: grants the tenant's runtime principal exactly what
/// request handling needs (CRUD on the module schemas, table creation inside <c>FormEngine</c> for
/// published forms, read access to its own boundary row) and nothing else. The principal itself is
/// created by provisioning (managed), by the customer's setup script (bring-your-own), or by the
/// operator (configuration tenants); this step refuses to invent one.
/// </summary>
public sealed class TenantRuntimeGrantsModule : ITenantDatabaseModule
{
    public const string Version = "20261101000000_RuntimeGrants_v1";

    private static readonly string[] Schemas = ["Auth", "FormEngine", "Task", "Workflow"];

    public string Name => "RuntimeGrants";

    public int Order => 1000;

    public string? LatestMigrationId => Version;

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var tenant = services.GetRequiredService<ITenantContext>().Current ?? throw TenantAccessException.Required();
        var principal = services.GetRequiredService<ITenantDatabaseCatalog>().FindDatabase(tenant.Id)?.RuntimePrincipal
            ?? throw new InvalidOperationException($"Tenant '{tenant.Key}' has no runtime principal registered.");
        var connectionString = services.GetRequiredService<ITenantConnectionStringResolver>().Resolve(tenant);
        if (!await GrantAsync(connectionString, principal, cancellationToken))
        {
            // A configuration tenant's login is created by its operator after the first migration
            // (scripts/provision-local-runtime.*). Until it exists the tenant API's verification
            // refuses the tenant, so skipping here never serves anyone under a weaker credential.
            services.GetRequiredService<ILogger<TenantRuntimeGrantsModule>>().LogWarning(
                "Runtime principal {Principal} does not exist yet in tenant {TenantKey}'s database; grants skipped.",
                principal, tenant.Key);
        }
    }

    public Task SeedAsync(IServiceProvider services, TenantSeedProfile profile, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<string>> GetPendingMigrationsAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    /// <returns>False when the principal does not exist in the database yet; nothing is granted then.</returns>
    public static async Task<bool> GrantAsync(string ownerConnectionString, string principal, CancellationToken cancellationToken)
    {
        if (!TenantDatabaseVerifier.IsSafePrincipalName(principal))
            throw new InvalidOperationException("The runtime principal name is not a safe SQL identifier.");

        await using var connection = new SqlConnection(ownerConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF USER_ID(@principal) IS NULL
                SELECT 0;
            ELSE IF IS_ROLEMEMBER(N'db_owner', @principal) = 1
                THROW 51301, 'The runtime principal must not be a database owner.', 1;
            ELSE
                SELECT 1;
            """;
        command.Parameters.Add("@principal", SqlDbType.NVarChar, 128).Value = principal;
        if (await command.ExecuteScalarAsync(cancellationToken) is not 1)
            return false;

        var quoted = "[" + principal.Replace("]", "]]", StringComparison.Ordinal) + "]";
        var grants = new List<string>
        {
            $"GRANT CONNECT TO {quoted};",
            $"GRANT VIEW DEFINITION ON USER::[nwfm_form_storage_owner] TO {quoted};",
            $"GRANT SELECT ON OBJECT::Tenancy.DeploymentBoundary TO {quoted};",
            $"GRANT VIEW DEFINITION ON OBJECT::Tenancy.LifecycleAudit TO {quoted};",
            // Dynamic form publication creates and alters only tables in the FormEngine schema.
            $"GRANT CREATE TABLE TO {quoted};",
            $"GRANT ALTER ON SCHEMA::FormEngine TO {quoted};"
        };
        grants.AddRange(Schemas.Select(schema => $"GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::[{schema}] TO {quoted};"));

        command.Parameters.Clear();
        command.CommandText = string.Join(Environment.NewLine, grants);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }
}
