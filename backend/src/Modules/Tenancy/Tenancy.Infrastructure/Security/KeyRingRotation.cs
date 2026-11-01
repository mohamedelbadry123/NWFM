using Microsoft.AspNetCore.DataProtection;

namespace Tenancy.Infrastructure.Security;

/// <summary>
/// The tenant API never generates Data Protection keys, so the control plane must keep a current
/// default key in the shared catalog ring. Protecting a probe makes the key ring create the first key,
/// and the successor ahead of the current key's expiry, the way any protect call would.
/// </summary>
public static class KeyRingRotation
{
    private const string Purpose = "NWFM.KeyRing.Rotation";

    /// <summary>Call from the control plane on start and periodically; never from the tenant API.</summary>
    public static void EnsureCurrentKey(IDataProtectionProvider provider) =>
        provider.CreateProtector(Purpose).Protect([1]);
}
