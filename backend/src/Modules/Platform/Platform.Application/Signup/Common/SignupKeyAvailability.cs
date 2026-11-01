using NWFM.Shared.Options;
using Tenancy.Application.Common.Interfaces;

namespace Platform.Application.Signup.Common;

public static class SignupKeyUnavailableReasons
{
    public const string Invalid = "invalid";
    public const string Reserved = "reserved";
    public const string Taken = "taken";
}

/// <summary>
/// Whether a workspace address can still be claimed. Advisory: the unique key on the tenant table is
/// what settles two signups racing for the same address, when provisioning reserves it.
/// </summary>
public static class SignupKeyAvailability
{
    public static async Task<string?> UnavailableReasonAsync(
        ICatalogDbContext catalog,
        TenancyOptions tenancy,
        string key,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        if (!TenantKey.IsValid(key))
        {
            return SignupKeyUnavailableReasons.Invalid;
        }

        if (tenancy.ReservedSubdomains.Contains(key, StringComparer.OrdinalIgnoreCase))
        {
            return SignupKeyUnavailableReasons.Reserved;
        }

        if (await catalog.Tenants.AnyAsync(t => t.Key == key, cancellationToken))
        {
            return SignupKeyUnavailableReasons.Taken;
        }

        // An unconfirmed signup holds its address only until its link expires.
        var heldBySignup = await catalog.SignupRequests.AnyAsync(s =>
                s.TenantKey == key
                && s.Status != SignupStatus.Rejected
                && s.Status != SignupStatus.Completed
                && (s.EmailVerifiedAtUtc != null || s.EmailVerificationExpiresAtUtc > utcNow),
            cancellationToken);

        return heldBySignup ? SignupKeyUnavailableReasons.Taken : null;
    }
}
