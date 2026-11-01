using NWFM.Shared.MultiTenancy;

namespace NWFM.Tests.Tenancy;

internal static class TestTenants
{
    public static TenantInfo Active(Guid? id = null, string key = "alpha", int? requestsPerMinute = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Key = key,
        Name = key,
        Status = TenantStatus.Active,
        RequestsPerMinute = requestsPerMinute
    };

    public static TenantInfo WithStatus(TenantStatus status, Guid? id = null, string key = "alpha") => new()
    {
        Id = id ?? Guid.NewGuid(),
        Key = key,
        Name = key,
        Status = status
    };

    /// <summary>Binds the calling flow to <paramref name="tenant"/> for the lifetime of the returned scope.</summary>
    public static IDisposable Bind(TenantInfo tenant) => new TenantContextAccessor().BeginScope(tenant);

    /// <summary>A context that always reports <paramref name="tenant"/>, for exercising one tenant's view of a singleton.</summary>
    public static ITenantContext Fixed(TenantInfo? tenant) => new FixedTenantContext(tenant);

    private sealed class FixedTenantContext(TenantInfo? tenant) : ITenantContext
    {
        public TenantInfo? Current => tenant;
        public bool IsResolved => tenant is not null;
        public bool IsPlatformScope => false;
        public void SetTenant(TenantInfo value) => throw new NotSupportedException();
        public void MarkPlatformScope() => throw new NotSupportedException();
        public IDisposable BeginRequestScope() => throw new NotSupportedException();
        public IDisposable BeginScope(TenantInfo value) => throw new NotSupportedException();
        public IDisposable BeginPlatformScope() => throw new NotSupportedException();
    }
}
