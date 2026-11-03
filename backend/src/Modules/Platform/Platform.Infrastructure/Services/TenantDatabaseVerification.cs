using Platform.Application.Common.Interfaces;
using Tenancy.Infrastructure.Boundary;
using Tenancy.Infrastructure.MultiTenancy;

namespace Platform.Infrastructure.Services;

internal sealed class TenantDatabaseVerification(ITenantDatabaseCatalog databases, ISecretProtector secrets)
    : ITenantDatabaseVerification
{
    public async Task<bool> VerifyRuntimeAsync(TenantInfo tenant, CancellationToken cancellationToken)
    {
        var record = databases.FindDatabase(tenant.Id)
            ?? throw new InvalidOperationException($"Tenant '{tenant.Key}' has no database registered.");
        if (record.Mode == TenantDatabaseMode.Configuration || string.IsNullOrWhiteSpace(record.EncryptedConnectionString))
            return false;

        var runtime = secrets.Unprotect(SecretPurposes.TenantRuntimeConnection, record.EncryptedConnectionString);
        await TenantDatabaseVerifier.VerifyRuntimeAsync(runtime, tenant.Id, cancellationToken);
        return true;
    }
}
