using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NWFM.Shared.Options;
using Tenancy.Domain.Entities;

namespace Tenancy.Infrastructure.MultiTenancy;

/// <summary>
/// Picks the credential this host is entitled to: the tenant API decrypts only the restricted runtime
/// connection, the control plane only the owner connection. A configuration-mode tenant names a
/// connection string each host defines with its own credential.
/// </summary>
internal sealed class TenantConnectionStringResolver(
    ITenantContext tenantContext,
    ITenantDatabaseCatalog databaseCatalog,
    ISecretProtector secretProtector,
    IOptions<TenancyOptions> options,
    IConfiguration configuration)
    : ITenantConnectionStringResolver
{
    private const string DesignTimeConnectionName = "DefaultConnection";

    private readonly ConcurrentDictionary<string, string> _decrypted = new(StringComparer.Ordinal);

    public string ResolveForCurrentTenant()
    {
        if (tenantContext.Current is { } tenant)
        {
            return Resolve(tenant);
        }

        // `dotnet ef migrations add` builds the context with no request and no tenant.
        if (EF.IsDesignTime)
        {
            return configuration.GetConnectionString(DesignTimeConnectionName)
                ?? throw new InvalidOperationException("Design-time migrations need ConnectionStrings:DefaultConnection.");
        }

        throw TenantAccessException.Required();
    }

    public string Resolve(TenantInfo tenant)
    {
        var record = databaseCatalog.FindDatabase(tenant.Id)
            ?? throw new InvalidOperationException($"Tenant '{tenant.Key}' has no database registered.");

        if (record.Mode == TenantDatabaseMode.Configuration)
        {
            return configuration.GetConnectionString(record.ConnectionStringName!)
                ?? throw new InvalidOperationException($"Connection string '{record.ConnectionStringName}' for tenant '{tenant.Key}' is not configured.");
        }

        var (encrypted, purpose) = options.Value.ConnectionRole == TenantConnectionRole.Administration
            ? (record.EncryptedAdminConnectionString, SecretPurposes.TenantAdminConnection)
            : (record.EncryptedConnectionString, SecretPurposes.TenantRuntimeConnection);
        if (string.IsNullOrWhiteSpace(encrypted))
            throw new InvalidOperationException($"Tenant '{tenant.Key}' has no {options.Value.ConnectionRole} connection string.");

        return _decrypted.GetOrAdd(purpose + ":" + encrypted, _ => secretProtector.Unprotect(purpose, encrypted));
    }
}
