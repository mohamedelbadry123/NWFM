namespace NWFM.Shared.MultiTenancy;

/// <summary>The flow cannot be bound to a serving tenant. Mapped to its code and status by both hosts.</summary>
public sealed class TenantAccessException(string message, string code, int statusCode) : Exception(message)
{
    public string Code { get; } = code;

    public int StatusCode { get; } = statusCode;

    public static TenantAccessException NotFound() =>
        new("The workspace could not be found.", TenancyErrorCodes.TenantNotFound, 404);

    public static TenantAccessException Required() =>
        new("A workspace is required for this request.", TenancyErrorCodes.TenantRequired, 400);

    public static TenantAccessException Mismatch() =>
        new("The requested workspace is not the workspace of this session.", TenancyErrorCodes.TenantMismatch, 400);

    public static TenantAccessException Suspended() =>
        new("This workspace is suspended. Contact support.", TenancyErrorCodes.TenantSuspended, 403);

    public static TenantAccessException Maintenance() =>
        new("This workspace is under maintenance. Please try again shortly.", TenancyErrorCodes.TenantMaintenance, 503);

    public static TenantAccessException SchemaUpgradePending() =>
        new("This workspace is being upgraded. Please try again shortly.", TenancyErrorCodes.TenantSchemaUpgradePending, 503);

    public static TenantAccessException Unverified() =>
        new("This workspace's database failed its isolation checks.", TenancyErrorCodes.TenantUnverified, 503);

    public static TenantAccessException SubscriptionInactive() =>
        new("This workspace's subscription is not active.", TenancyErrorCodes.TenantSubscriptionInactive, 403);

    /// <summary>The failure that keeps <paramref name="tenant"/> from serving, or null when it may.</summary>
    public static TenantAccessException? ForStatus(TenantInfo tenant) => tenant.Status switch
    {
        TenantStatus.Active when !tenant.IsSubscriptionUsable => SubscriptionInactive(),
        TenantStatus.Active => null,
        TenantStatus.Suspended => Suspended(),
        TenantStatus.Maintenance => Maintenance(),
        _ => NotFound()
    };
}
