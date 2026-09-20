namespace FormEngine.Domain.Constants;

/// <summary>
/// Client shapes a published form version can be assembled for. One version row is produced per
/// target client on publish; today only <see cref="Formly"/> is written.
/// </summary>
public static class FormTargetClients
{
    public const string Formly = "Formly";
    public const string Mobile = "Mobile";

    public static readonly IReadOnlyList<string> All =
    [
        Formly,
        Mobile
    ];

    public static bool IsDefined(string? value) =>
        value is not null && All.Contains(value, StringComparer.Ordinal);
}
