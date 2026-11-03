namespace NWFM.Shared.MultiTenancy;

/// <summary>Lifecycle of a tenant in the catalog. Only <see cref="Active"/> serves traffic.</summary>
public enum TenantStatus
{
    Provisioning = 1,
    Active = 2,
    Suspended = 3,

    /// <summary>A migration failed or an operator paused the tenant; it is refused rather than served on a mismatched schema.</summary>
    Maintenance = 4,

    Deleted = 5
}
