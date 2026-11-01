namespace NWFM.Shared.MultiTenancy;

/// <summary>
/// <see cref="ITenantContext"/> over a mutable holder in an <see cref="AsyncLocal{T}"/>, shaped like
/// <c>HttpContextAccessor</c>: a tenant bound inside the authentication handler stays visible to the
/// middleware and filters that run after it.
/// </summary>
public sealed class TenantContextAccessor : ITenantContext
{
    private static readonly AsyncLocal<Holder?> Ambient = new();

    public TenantInfo? Current => Ambient.Value?.Tenant;

    public bool IsResolved => Current is not null;

    public bool IsPlatformScope => Ambient.Value?.IsPlatformScope ?? false;

    public void SetTenant(TenantInfo tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        var holder = Ambient.Value ??= new Holder();
        if (holder.IsPlatformScope || holder.Tenant is { } existing && existing.Id != tenant.Id)
            throw TenantAccessException.Mismatch();
        holder.Tenant = tenant;
    }

    public void MarkPlatformScope()
    {
        var holder = Ambient.Value ??= new Holder();
        if (holder.Tenant is not null)
            throw TenantAccessException.Mismatch();
        holder.IsPlatformScope = true;
    }

    public IDisposable BeginRequestScope() => Push(new Holder());

    public IDisposable BeginScope(TenantInfo tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        return Push(new Holder { Tenant = tenant });
    }

    public IDisposable BeginPlatformScope() => Push(new Holder { IsPlatformScope = true });

    private static Restore Push(Holder holder)
    {
        var previous = Ambient.Value;
        Ambient.Value = holder;
        return new Restore(previous);
    }

    private sealed class Holder
    {
        public TenantInfo? Tenant { get; set; }
        public bool IsPlatformScope { get; set; }
    }

    private sealed class Restore(Holder? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Ambient.Value = previous;
        }
    }
}
