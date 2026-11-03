namespace NWFM.Shared.MultiTenancy;

/// <summary>
/// The ambient tenant of the current logical flow (request, worker pass, provisioning step).
/// Backed by an <see cref="AsyncLocal{T}"/> holder so singletons (cache, file storage, rate limits)
/// see the same tenant as scoped services. A flow is bound once and can never change tenant.
/// </summary>
public interface ITenantContext
{
    TenantInfo? Current { get; }

    bool IsResolved { get; }

    /// <summary>The flow deliberately has no tenant (platform console, public signup). No tenant database is reachable from it.</summary>
    bool IsPlatformScope { get; }

    /// <summary>Binds the flow. Re-binding the same tenant is a no-op; another tenant, or any tenant in a platform scope, throws.</summary>
    void SetTenant(TenantInfo tenant);

    void MarkPlatformScope();

    /// <summary>Starts an empty holder for a new request; disposing restores the previous one.</summary>
    IDisposable BeginRequestScope();

    /// <summary>Runs the enclosed work as <paramref name="tenant"/> (workers, provisioning, migrations).</summary>
    IDisposable BeginScope(TenantInfo tenant);

    IDisposable BeginPlatformScope();
}
