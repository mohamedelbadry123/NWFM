namespace Tenancy.Domain.Constants;

/// <summary>Roles carried by platform tokens. Unrelated to tenant roles — the two token schemes never mix.</summary>
public static class PlatformRoles
{
    /// <summary>Manages tenants, plans, subscriptions, database servers and platform users.</summary>
    public const string Admin = "PlatformAdmin";

    /// <summary>Reads tenants and opens consented support sessions.</summary>
    public const string Support = "PlatformSupport";

    public static IReadOnlyList<string> All { get; } = [Admin, Support];

    public static bool IsDefined(string? role) => role is not null && All.Contains(role, StringComparer.Ordinal);
}

/// <summary>Authorization policy names for the platform API. Evaluated against the platform scheme only.</summary>
public static class PlatformPolicies
{
    public const string Admin = "Platform.Admin";
    public const string Staff = "Platform.Staff";
}
