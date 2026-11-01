namespace NWFM.Shared.MultiTenancy;

/// <summary>Error codes for tenancy and support-access refusals. Shared by both hosts and the web apps.</summary>
public static class TenancyErrorCodes
{
    public const string TenantNotFound = "Tenant.NotFound";
    public const string TenantRequired = "Tenant.Required";

    /// <summary>The token's tenant differs from the host, header or selector the request arrived with.</summary>
    public const string TenantMismatch = "Tenant.Mismatch";

    public const string TenantSuspended = "Tenant.Suspended";
    public const string TenantMaintenance = "Tenant.Maintenance";
    public const string TenantSchemaUpgradePending = "Tenant.SchemaUpgradePending";
    public const string TenantUnverified = "Tenant.Unverified";
    public const string TenantSubscriptionInactive = "Tenant.SubscriptionInactive";
    public const string TenantInactive = "Tenant.Inactive";
    public const string PlanLimitReached = "Tenant.PlanLimitReached";

    /// <summary>A write was attempted inside a read-only support session.</summary>
    public const string SupportSessionReadOnly = "Support.SessionReadOnly";

    public const string SupportAccessNotGranted = "Support.AccessNotGranted";
}
