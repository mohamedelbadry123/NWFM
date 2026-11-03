namespace Tenancy.Domain.Entities;

/// <summary>An explicit host name (custom domain or legacy host) mapped to a tenant.</summary>
public sealed class TenantHost
{
    public const int HostMaxLength = 253;

    private TenantHost()
    {
    }

    private TenantHost(Guid tenantId, string host)
    {
        TenantId = tenantId;
        Host = host;
    }

    public int Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Host { get; private set; } = default!;

    public static string Normalize(string? host)
    {
        var value = (host ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();
        var portIndex = value.IndexOf(':');
        return portIndex >= 0 ? value[..portIndex] : value;
    }

    internal static TenantHost Create(Guid tenantId, string normalizedHost)
    {
        if (string.IsNullOrWhiteSpace(normalizedHost) || normalizedHost.Length > HostMaxLength)
        {
            throw new DomainException("A host name is required and must be a valid DNS name.");
        }

        return new TenantHost(tenantId, normalizedHost);
    }
}
