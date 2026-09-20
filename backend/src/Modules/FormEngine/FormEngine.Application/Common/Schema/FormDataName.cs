using System.Text.RegularExpressions;

namespace FormEngine.Application.Common.Schema;

/// <summary>
/// The one rule a <c>data_name</c> has to satisfy: it becomes a SQL column, so it has to be a legal
/// unquoted identifier. Publish enforces it, and the submission store whitelists against it before
/// any identifier reaches a statement — both call here, because two copies drifting apart is how a
/// name gets past publish and then silently fails to write.
/// </summary>
public static partial class FormDataName
{
    /// <summary>Reads back to the user when a name is rejected, so the fix is obvious.</summary>
    public const string RuleDescription =
        "a data_name must start with a letter or underscore and contain only letters, digits and underscores";

    public static bool IsValid(string? dataName) =>
        !string.IsNullOrWhiteSpace(dataName) && IdentifierRegex().IsMatch(dataName);

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();
}
