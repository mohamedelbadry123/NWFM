namespace NWFM.Shared.MultiTenancy;

/// <summary>JWT claims that carry tenancy and support-session identity.</summary>
public static class TenantClaimTypes
{
    /// <summary>The catalog tenant id a tenant token was issued for. Exactly one per token.</summary>
    public const string TenantId = "tid";

    /// <summary>RFC 8693 actor: the platform user acting inside the tenant during a support session.</summary>
    public const string Actor = "act";

    public const string ActorName = "act_name";

    /// <summary><see cref="SupportScopes.ReadOnly"/> or <see cref="SupportScopes.Full"/>.</summary>
    public const string SupportScope = "support_scope";

    public const string SupportSessionId = "support_sid";
}

public static class SupportScopes
{
    public const string ReadOnly = "readonly";
    public const string Full = "full";
}

public static class TenantHttpHeaders
{
    /// <summary>
    /// Tenant key hint for anonymous development calls. Never overrides the <see cref="TenantClaimTypes.TenantId"/>
    /// claim and is ignored unless <c>Tenancy:AllowTenantHeader</c> is on.
    /// </summary>
    public const string Tenant = "X-Tenant";
}

/// <summary>Prefixes a refresh token with its tenant so the anonymous refresh call can resolve it.</summary>
public static class TenantRefreshTokens
{
    public const char Separator = '.';

    public static string Compose(Guid tenantId, string random) => $"{tenantId:N}{Separator}{random}";

    public static bool TryReadTenantId(string? token, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(token)) return false;
        var index = token.IndexOf(Separator);
        return index > 0 && Guid.TryParseExact(token[..index], "N", out tenantId);
    }
}
