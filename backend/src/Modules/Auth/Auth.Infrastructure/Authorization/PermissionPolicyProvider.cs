using NWFM.Shared.Constants;
using Auth.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Authorization;

public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(PermissionPolicies.AnyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string[] codes = policyName[PermissionPolicies.AnyPrefix.Length..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (codes.Length == 0) return _fallback.GetPolicyAsync(policyName);
            AuthorizationPolicy policy = new AuthorizationPolicyBuilder()
                .AddRequirements(new AnyPermissionRequirement(codes))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        if (!NwfmPolicies.All.Contains(policyName, StringComparer.Ordinal))
            return _fallback.GetPolicyAsync(policyName);

        AuthorizationPolicy permissionPolicy = new AuthorizationPolicyBuilder()
            .AddRequirements(new PermissionRequirement(policyName))
            .Build();
        return Task.FromResult<AuthorizationPolicy?>(permissionPolicy);
    }
}
