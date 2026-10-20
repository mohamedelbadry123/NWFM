using System.Security.Cryptography;
using System.Text;

namespace Auth.Domain.Options;

public sealed class ExternalConsumersOptions
{
    public const string SectionName = "ExternalConsumers";

    public List<ExternalConsumerApp> WFM { get; set; } = [];
    public List<ExternalConsumerApp> C2M { get; set; } = [];
    public List<ExternalConsumerApp> Internal { get; set; } = [];
    public List<ExternalConsumerApp> Mob { get; set; } = [];

    public ExternalConsumerApp? FindAuthorizedConsumer(
        string apiKey, Guid tenantId, string environment, string requiredScope)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(environment) ||
            string.IsNullOrWhiteSpace(requiredScope) || string.IsNullOrWhiteSpace(apiKey) ||
            apiKey.Length < 32 || apiKey.Length > 512)
            return null;

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return All().FirstOrDefault(c =>
            c.IsEnabled && !string.IsNullOrWhiteSpace(c.AppID) &&
            c.TenantId == tenantId &&
            string.Equals(c.Environment, environment, StringComparison.Ordinal) &&
            c.Scopes.Contains(requiredScope, StringComparer.Ordinal) &&
            MatchesHash(digest, c.ApiKeySha256));
    }

    private static bool MatchesHash(byte[] digest, string configuredHash)
    {
        if (configuredHash.Length != 64) return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(digest, Convert.FromHexString(configuredHash));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private IEnumerable<ExternalConsumerApp> All() =>
        WFM.Concat(C2M).Concat(Internal).Concat(Mob);
}

public sealed class ExternalConsumerApp
{
    public string AppID { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    /// <summary>SHA-256 of a cryptographically random, high-entropy API credential.</summary>
    public string ApiKeySha256 { get; set; } = string.Empty;
    public string SystemName { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string Environment { get; set; } = string.Empty;
    public List<string> Scopes { get; set; } = [];
    public bool IsEnabled { get; set; }
}
