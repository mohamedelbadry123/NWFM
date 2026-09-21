namespace Tasks.Application.TaskTypes.Models;

public sealed class TaskTypeDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = default!;
    public string NameEn { get; init; } = default!;
    public string NameAr { get; init; } = default!;
    public string? DescriptionEn { get; init; }
    public string? DescriptionAr { get; init; }

    public Guid FormDefinitionId { get; init; }
    public string? FormCode { get; init; }
    public string? FormNameEn { get; init; }
    public string? FormNameAr { get; init; }

    /// <summary>The version a task raised now would pin; null when the form has none that accepts fills.</summary>
    public int? FormCurrentVersionNo { get; init; }

    public string? DepartmentCode { get; init; }
    public int? FillSlaHours { get; init; }
    public int? CompletionSlaHours { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>A published form a task type can be bound to.</summary>
public sealed record FormOptionDto(Guid Id, string Code, string NameEn, string NameAr, int CurrentVersionNo);
