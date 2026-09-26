using FormEngine.Application.Common.Schema;

namespace FormEngine.Application.Uploads.Common;

/// <summary>
/// What may be stored for a media field. The picker's <c>accept</c> attribute and the client's own
/// check are only hints — drag-drop, "All files" and a hand-rolled request all bypass them — so the
/// rules are applied here, where they cannot be skipped.
/// </summary>
internal static class FormFileRules
{
    public const int BytesPerMb = 1024 * 1024;

    private const int MaxExtensionLength = 16;

    /// <summary>An empty allow-list means "anything"; entries may be exact or <c>image/*</c>-style.</summary>
    public static bool IsAllowedContentType(string? contentType, IReadOnlyList<string> allowed)
    {
        if (allowed.Count == 0)
        {
            return true;
        }

        var value = contentType ?? string.Empty;

        return allowed.Any(pattern => pattern.EndsWith("/*", StringComparison.Ordinal)
            ? value.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Keeps a short, dot-prefixed alphanumeric extension; drops anything else.</summary>
    public static string SafeExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension) || extension.Length > MaxExtensionLength)
        {
            return string.Empty;
        }

        return extension.Skip(1).All(char.IsLetterOrDigit) ? extension.ToLowerInvariant() : string.Empty;
    }

    /// <summary>
    /// Checks the file against the extensions its own field declares.
    /// </summary>
    /// <remarks>
    /// A <c>data_name</c> the schema does not mention stays permitted: a draft or an older published
    /// version legitimately uploads against names the parser drops, and rejecting those would break
    /// a fill that used to work.
    /// </remarks>
    public static bool IsAllowedExtension(
        FormSchema schema,
        string dataName,
        string extension,
        out IReadOnlyList<string> allowed)
    {
        allowed = [];

        var field = schema.Fields
            .FirstOrDefault(x => string.Equals(x.DataName, dataName, StringComparison.OrdinalIgnoreCase));

        if (field is null || field.AllowedExtensions.Count == 0)
        {
            return true;
        }

        allowed = field.AllowedExtensions;

        // `SafeExtension` returns '.pdf' or ''; the field's list holds 'pdf'.
        return extension.Length > 1 && allowed.Contains(extension[1..], StringComparer.OrdinalIgnoreCase);
    }
}
