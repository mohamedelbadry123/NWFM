namespace FormEngine.Application.Forms.Models;

/// <summary>
/// A form that can be filled: it has a published version and is neither deprecated nor archived.
/// Feeds the fill picker, and is the list a workflow user task will pick a form and version from.
/// </summary>
public sealed class PublishedFormDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = default!;
    public string NameEn { get; init; } = default!;
    public string NameAr { get; init; } = default!;
    public string Category { get; init; } = default!;
    public string Status { get; init; } = default!;
    public string? DepartmentCode { get; init; }
    public int CurrentVersionNo { get; init; }

    /// <summary>Every published version number, newest first — so a picker can pin an older one.</summary>
    public IReadOnlyList<int> VersionNos { get; init; } = [];
}
