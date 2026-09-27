namespace FormEngine.Domain.Constants;

/// <summary>
/// Lifecycle states of a form definition.
/// </summary>
public static class FormStatuses
{
    public const string Draft = "DRAFT";
    public const string Published = "PUBLISHED";
    public const string Deprecated = "DEPRECATED";
    public const string Archived = "ARCHIVED";

    public static readonly IReadOnlyList<string> All =
    [
        Draft,
        Published,
        Deprecated,
        Archived
    ];

    public static bool IsDefined(string? value) =>
        value is not null && All.Contains(value, StringComparer.Ordinal);

    /// <summary>Deprecated and archived forms accept no edits and no new submissions.</summary>
    public static bool IsFrozen(string? value) => value is Deprecated or Archived;
}
