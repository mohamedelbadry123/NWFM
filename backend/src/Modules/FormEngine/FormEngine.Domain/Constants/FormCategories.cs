namespace FormEngine.Domain.Constants;

/// <summary>
/// Business categories a form definition can belong to.
/// </summary>
public static class FormCategories
{
    public const string General = "GENERAL";
    public const string Inspection = "INSPECTION";
    public const string Survey = "SURVEY";
    public const string Request = "REQUEST";
    public const string Checklist = "CHECKLIST";
    public const string Other = "OTHER";

    public static readonly IReadOnlyList<string> All =
    [
        General,
        Inspection,
        Survey,
        Request,
        Checklist,
        Other
    ];

    public static bool IsDefined(string? value) =>
        value is not null && All.Contains(value, StringComparer.Ordinal);
}
