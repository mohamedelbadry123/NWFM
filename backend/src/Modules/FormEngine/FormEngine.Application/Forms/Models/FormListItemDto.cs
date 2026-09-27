namespace FormEngine.Application.Forms.Models;

/// <summary>One row of the forms grid. The schema JSON never rides along on a list read.</summary>
public sealed class FormListItemDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = default!;
    public string NameEn { get; init; } = default!;
    public string NameAr { get; init; } = default!;
    public string Category { get; init; } = default!;
    public string Status { get; init; } = default!;
    public string? DepartmentCode { get; init; }
    public int? CurrentVersionNo { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}
