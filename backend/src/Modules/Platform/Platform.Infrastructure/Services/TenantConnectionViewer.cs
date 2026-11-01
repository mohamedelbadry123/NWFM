using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Platform.Application.Common.Interfaces;

namespace Platform.Infrastructure.Services;

/// <inheritdoc />
internal sealed class TenantConnectionViewer(
    ITenantConnectionStringResolver connectionStrings,
    ILogger<TenantConnectionViewer> logger)
    : ITenantConnectionViewer
{
    private const string PasswordMask = "********";

    public TenantConnectionDto? Describe(TenantInfo tenant, bool reveal)
    {
        string connectionString;
        SqlConnectionStringBuilder builder;

        try
        {
            connectionString = connectionStrings.Resolve(tenant);
            builder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException
                                       or System.Security.Cryptography.CryptographicException)
        {
            // Never log the string itself: the failure may be in the middle of it.
            logger.LogWarning("The connection string of tenant {TenantKey} could not be read: {Error}", tenant.Key, ex.GetType().Name);
            return null;
        }

        var masked = new SqlConnectionStringBuilder(builder.ConnectionString);
        if (!string.IsNullOrEmpty(masked.Password))
        {
            masked.Password = PasswordMask;
        }

        return new TenantConnectionDto
        {
            Server = builder.DataSource,
            Database = builder.InitialCatalog,
            UserId = string.IsNullOrEmpty(builder.UserID) ? null : builder.UserID,
            IntegratedSecurity = builder.IntegratedSecurity,
            Masked = masked.ConnectionString,
            Full = reveal ? connectionString : null
        };
    }
}
