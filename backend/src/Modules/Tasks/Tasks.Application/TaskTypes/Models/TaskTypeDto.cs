namespace Tasks.Application.TaskTypes.Models;

public sealed class TaskTypeDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = default!;
    public string NameEn { get; init; } = default!;
    public string NameAr { get; init; } = default!;
    public string? DescriptionEn { get; init; }
    public string? DescriptionAr { get; init; }

    /// <summary>The forms its tasks are filled with, in order.</summary>
    public IReadOnlyList<TaskTypeFormDto> Forms { get; init; } = [];

    public string? DepartmentCode { get; init; }
    public int? FillSlaHours { get; init; }
    public int? CompletionSlaHours { get; init; }
    public bool ClosesC2mActivity { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// One of a type's forms. <see cref="CurrentVersionNo"/> is the version a task raised now would pin;
/// null when the form has none that accepts fills.
/// </summary>
public sealed record TaskTypeFormDto(
    Guid FormDefinitionId,
    string? Code,
    string? NameEn,
    string? NameAr,
    int? CurrentVersionNo,
    int SortOrder,
    bool IsC2mClosingForm);

/// <summary>A published form a task type — or a single task — can be bound to.</summary>
public sealed record FormOptionDto(Guid Id, string Code, string NameEn, string NameAr, int CurrentVersionNo);
