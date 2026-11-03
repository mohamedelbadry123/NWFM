using Microsoft.AspNetCore.DataProtection;
using NWFM.Shared.MultiTenancy;

namespace Auth.Infrastructure.Identity.Sso;

/// <summary>
/// Cookie options are singletons, but the payloads they protect belong to one tenant. Every
/// protect/unprotect call derives a sub-protector for the ambient tenant, so a temporary SSO cookie
/// issued for one tenant can never be read while serving another.
/// </summary>
internal sealed class TenantAwareDataProtector(IDataProtector root, ITenantContext tenants) : IDataProtector
{
    public IDataProtector CreateProtector(string purpose) => new TenantAwareDataProtector(root.CreateProtector(purpose), tenants);

    public byte[] Protect(byte[] plaintext) => ForTenant().Protect(plaintext);

    public byte[] Unprotect(byte[] protectedData) => ForTenant().Unprotect(protectedData);

    private IDataProtector ForTenant()
    {
        var tenant = tenants.Current ?? throw TenantAccessException.Required();
        return root.CreateProtector(tenant.Id.ToString("D"));
    }
}
