using System.Text;

namespace NWFM.Shared.Security;

/// <summary>
/// Refuses an HMAC signing key that is short or still the shipped placeholder. One key signs every
/// tenant's tokens and the signed <c>tid</c> claim is what binds a request to its tenant, so a key
/// copied from a sample file would let anyone mint a token for any workspace.
/// </summary>
public static class SigningKeyGuard
{
    public const int MinimumBytes = 32;

    private static readonly string[] PlaceholderMarkers = ["REPLACE_WITH", "CHANGE_ME", "SET_VIA_"];

    /// <summary>Throws when <paramref name="key"/> is unusable; <paramref name="settingName"/> names it in the message.</summary>
    public static void EnsureUsable(string? key, string settingName)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException($"{settingName} is required.");
        }

        if (PlaceholderMarkers.Any(marker => key.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"{settingName} is still the sample placeholder. Generate one with `openssl rand -base64 48`.");
        }

        if (Encoding.UTF8.GetByteCount(key) < MinimumBytes)
        {
            throw new InvalidOperationException($"{settingName} must be at least {MinimumBytes} bytes.");
        }
    }
}
