using System.Text.RegularExpressions;

namespace Tenancy.Domain.Entities;

/// <summary>
/// Rules for a tenant key. The key is a DNS label (it becomes the subdomain), a folder name and part
/// of a database name, so it is held to the narrowest form all three accept.
/// </summary>
public static partial class TenantKey
{
    public const int MinLength = 3;
    public const int MaxLength = 32;

    public static string Normalize(string? key) => (key ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsValid(string? key)
    {
        var normalized = Normalize(key);

        return normalized.Length is >= MinLength and <= MaxLength
            && Pattern().IsMatch(normalized)
            && !normalized.Contains("--", StringComparison.Ordinal);
    }

    [GeneratedRegex("^[a-z][a-z0-9-]*[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
