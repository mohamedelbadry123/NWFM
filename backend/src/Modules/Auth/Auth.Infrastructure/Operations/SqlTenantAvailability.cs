using System.Data;
using Microsoft.Data.SqlClient;
using NWFM.Shared.Abstractions;
using NWFM.Shared.MultiTenancy;

namespace Auth.Infrastructure.Operations;

/// <summary>
/// Reads the ambient tenant's own kill switch (<c>Tenancy.DeploymentBoundary.IsActive</c>) without
/// caching, so an operator deactivation applies before the catalog snapshot refreshes.
/// </summary>
public sealed class SqlTenantAvailability(ITenantConnectionStringResolver connections, ITenantContext tenants) : ITenantAvailability
{
    public async Task<bool> IsActiveAsync(CancellationToken cancellationToken = default)
    {
        if (tenants.Current is not { IsServing: true } tenant) return false;
        await using var connection = new SqlConnection(connections.Resolve(tenant));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT IsActive FROM Tenancy.DeploymentBoundary WHERE SingletonId=1 AND TenantId=@tenant";
        command.Parameters.Add("@tenant", SqlDbType.UniqueIdentifier).Value = tenant.Id;
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }
}
