namespace NWFM.Shared.MultiTenancy;

/// <summary>
/// Read side of the tenant catalog: an in-process snapshot refreshed on a short interval.
/// Synchronous because it is read from DbContext option factories, which cannot await.
/// </summary>
public interface ITenantStore
{
    TenantInfo? FindById(Guid id);

    TenantInfo? FindByKey(string key);

    /// <summary>Matches a registered host name, or <c>{key}.{Tenancy:RootDomain}</c>.</summary>
    TenantInfo? FindByHost(string host);

    IReadOnlyList<TenantInfo> GetAll();

    /// <summary>Drops this node's snapshot so the next read reloads it.</summary>
    void Invalidate();
}
