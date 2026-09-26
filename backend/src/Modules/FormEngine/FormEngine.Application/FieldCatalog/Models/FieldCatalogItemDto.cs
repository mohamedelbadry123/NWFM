namespace FormEngine.Application.FieldCatalog.Models;

/// <summary>
/// One data name in use across the published forms. The builder's Data Name box autocompletes
/// against these, so a name already meaning something elsewhere is offered with its type — reusing
/// it keeps answers comparable across forms, even though each form stores them in its own table.
/// </summary>
public sealed class FieldCatalogItemDto
{
    /// <summary>Stable per data name: the id of the first form field registered under it.</summary>
    public Guid Id { get; init; }

    public string DataName { get; init; } = default!;

    /// <summary>The type the name was first published as; see <see cref="HasTypeConflict"/>.</summary>
    public string FieldType { get; init; } = default!;

    public string? LabelEn { get; init; }
    public string? LabelAr { get; init; }

    /// <summary>Kept for the client's shape. Form fields carry no description, so this is always null.</summary>
    public string? Description { get; init; }

    /// <summary>How many forms store a column under this name.</summary>
    public int FormCount { get; init; }

    /// <summary>
    /// True when forms use the name as more than one type. Allowed — each form has its own table — but
    /// the answers are then not comparable across those forms, which is worth seeing.
    /// </summary>
    public bool HasTypeConflict { get; init; }

    /// <summary>Every type the name is published as, first-seen first.</summary>
    public IReadOnlyList<string> FieldTypes { get; init; } = [];
}
