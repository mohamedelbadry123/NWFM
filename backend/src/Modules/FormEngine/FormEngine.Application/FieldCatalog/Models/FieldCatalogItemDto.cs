namespace FormEngine.Application.FieldCatalog.Models;

/// <summary>
/// One canonical field name. The builder's Data Name box autocompletes against these, so reusing a
/// name reuses its <c>FE.Submissions</c> column across forms.
/// </summary>
public sealed class FieldCatalogItemDto
{
    public Guid Id { get; init; }
    public string DataName { get; init; } = default!;
    public string FieldType { get; init; } = default!;
    public string? LabelEn { get; init; }
    public string? LabelAr { get; init; }
    public string? Description { get; init; }
}
