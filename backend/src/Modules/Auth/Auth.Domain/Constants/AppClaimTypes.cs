namespace Auth.Domain.Constants;

public static class AppClaimTypes
{
    public const string TeamId = "team_id";
    public const string UserCode = "user_code";
    /// <summary>The catalog tenant the token was issued for. Same claim the tenant resolution binds by.</summary>
    public const string TenantId = NWFM.Shared.MultiTenancy.TenantClaimTypes.TenantId;
    public const string SessionId = "session_id";
    public const string SecurityStamp = "security_stamp";
}
