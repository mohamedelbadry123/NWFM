namespace FormEngine.Application.Forms.Models;

public sealed class FormDetailDto
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
    public string? CreatedBy { get; init; }
    public string? UpdatedBy { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    /// <summary>The working form-builder document (name_en/name_ar/elements). The client maps it to formly.</summary>
    public string SchemaJson { get; init; } = "{}";
}
