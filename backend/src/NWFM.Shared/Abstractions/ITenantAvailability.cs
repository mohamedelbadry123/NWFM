namespace NWFM.Shared.Abstractions;

/// <summary>Reads the authoritative deployment lifecycle state without caching authorization.</summary>
public interface ITenantAvailability
{
    Task<bool> IsActiveAsync(CancellationToken cancellationToken = default);
}
