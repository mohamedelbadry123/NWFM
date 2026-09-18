namespace NWFM.Shared.MultiTenancy;

public interface ITenantAware
{
    Guid OrganizationId { get; }
}
