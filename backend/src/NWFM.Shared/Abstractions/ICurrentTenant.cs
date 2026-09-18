namespace NWFM.Shared.Abstractions;

public interface ICurrentTenant
{
    Guid OrganizationId { get; }
}
