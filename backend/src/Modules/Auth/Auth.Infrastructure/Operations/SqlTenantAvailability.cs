using System.Data;
using Microsoft.Data.SqlClient;
using NWFM.Shared.Abstractions;

namespace Auth.Infrastructure.Operations;

public sealed class SqlTenantAvailability(string connectionString, ICurrentTenant tenant) : ITenantAvailability
{
    public async Task<bool> IsActiveAsync(CancellationToken cancellationToken = default)
    {
        if (tenant.OrganizationId == Guid.Empty) return false;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT IsActive FROM Tenancy.DeploymentBoundary WHERE SingletonId=1 AND TenantId=@tenant";
        command.Parameters.Add("@tenant", SqlDbType.UniqueIdentifier).Value = tenant.OrganizationId;
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }
}
