namespace NWFM.Shared.MultiTenancy;

/// <summary>
/// On-disk layout of tenant media: each tenant owns <c>{FileStorage:Root}/{tenantKey}</c> unless the
/// catalog names an explicit root, so a tenant can be backed up or removed as a single folder.
/// </summary>
public static class TenantStorageLayout
{
    /// <summary>Rooted paths stand as they are; relative ones resolve against the content root.</summary>
    public static string ResolveRoot(string configured, string contentRoot) =>
        Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(contentRoot, configured));

    /// <summary>
    /// True when <paramref name="tenantKey"/> is a single, safe folder name. Keys are validated slugs
    /// already; this second guard keeps a bad value from pointing outside the storage root.
    /// </summary>
    public static bool IsSafeKey(string? tenantKey) =>
        !string.IsNullOrWhiteSpace(tenantKey)
        && tenantKey.Trim() == tenantKey
        && !tenantKey.StartsWith('.')
        && tenantKey.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && tenantKey.IndexOfAny(['/', '\\', ':']) < 0;

    /// <summary>The media root of <paramref name="tenant"/>.</summary>
    public static string TenantRoot(string storageRoot, string contentRoot, TenantInfo tenant)
    {
        if (!string.IsNullOrWhiteSpace(tenant.StoragePath))
            return ResolveRoot(tenant.StoragePath, contentRoot);
        if (!IsSafeKey(tenant.Key))
            throw new ArgumentException($"Tenant key '{tenant.Key}' is not a safe folder name.", nameof(tenant));
        return Path.GetFullPath(Path.Combine(ResolveRoot(storageRoot, contentRoot), tenant.Key));
    }
}
