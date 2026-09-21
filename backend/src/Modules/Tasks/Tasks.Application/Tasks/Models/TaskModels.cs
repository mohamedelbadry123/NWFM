namespace Tasks.Application.Tasks.Models;

/// <summary>One row of the task worklist.</summary>
public sealed class TaskListItemDto
{
    public Guid Id { get; init; }
    public string TaskNumber { get; init; } = default!;

    public Guid TaskTypeId { get; init; }
    public string? TaskTypeCode { get; init; }
    public string? TaskTypeNameEn { get; init; }
    public string? TaskTypeNameAr { get; init; }

    public Guid FormDefinitionId { get; init; }

    /// <summary>The version pinned to the task.</summary>
    public int FormVersionNo { get; init; }

    /// <summary>The form's current version — ahead of <see cref="FormVersionNo"/> when a newer one was published since.</summary>
    public int? FormCurrentVersionNo { get; init; }

    public string Status { get; init; } = default!;
    public string Priority { get; init; } = default!;
    public string Source { get; init; } = default!;
    public string? Title { get; init; }
    public string? ExternalReference { get; init; }

    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public string? Address { get; init; }
    public string? CbuCode { get; init; }
    public string? BranchCode { get; init; }
    public string? OperationAreaCode { get; init; }
    public string? DepartmentCode { get; init; }

    public Guid? AssignedTeamId { get; init; }
    public string? AssignedTeamName { get; init; }

    public DateTime? DueDate { get; init; }
    public DateTime? CompletionDueDate { get; init; }
    public DateTime? AssignedDate { get; init; }
    public DateTime? SubmittedDate { get; init; }
    public int SubmissionCount { get; init; }
    public string? ReturnReasonCode { get; init; }
    public string? ReturnReason { get; init; }
    public DateTime? ReturnedDate { get; init; }
    public int ReturnCount { get; init; }

    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>A task opened on its own: everything on the row, its assignments, and the form it is filled with.</summary>
public sealed class TaskDetailDto
{
    public TaskListItemDto Task { get; init; } = default!;

    public string? Notes { get; init; }
    public int? FillSlaHours { get; init; }
    public int? CompletionSlaHours { get; init; }
    public string? AssignedBy { get; init; }
    public string? LastFilledBy { get; init; }
    public string? CompletedBy { get; init; }
    public DateTime? CompletedDate { get; init; }
    public string? ReturnedBy { get; init; }
    public string? ExpiredBy { get; init; }
    public DateTime? ExpiredDate { get; init; }
    public string? CreatedBy { get; init; }

    public string? FormCode { get; init; }
    public string? FormNameEn { get; init; }
    public string? FormNameAr { get; init; }

    /// <summary>The pinned version's form-builder document — what the fill and preview dialogs render.</summary>
    public string? SchemaJson { get; init; }

    public IReadOnlyList<TaskAssignmentDto> Assignments { get; init; } = [];
}

public sealed class TaskAssignmentDto
{
    public Guid Id { get; init; }
    public Guid TeamId { get; init; }
    public string? TeamName { get; init; }
    public string Status { get; init; } = default!;
    public string? AssignedBy { get; init; }
    public DateTime AssignedDate { get; init; }
    public DateTime? DueDate { get; init; }
    public DateTime? SubmittedDate { get; init; }
    public string? Note { get; init; }
    public bool IsActive { get; init; }
}

public sealed record TaskHistoryDto(
    Guid Id,
    string? FromStatus,
    string ToStatus,
    string? ChangedBy,
    DateTime ChangedDate,
    string? Note);

/// <summary>One fill of the task's form, with its answers as the form's table stores them.</summary>
public sealed record TaskFillDto(
    Guid SubmissionId,
    int VersionNo,
    string? SubmittedBy,
    string? SubmittedByName,
    DateTimeOffset? SubmittedDate,
    IReadOnlyDictionary<string, object?> Answers);

public sealed record TaskFileDto(
    Guid FileId,
    Guid? SubmissionId,
    string DataName,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Status,
    DateTime CreatedAt);

/// <summary>A team that may take the task, and how much it already holds.</summary>
public sealed record EligibleTeamDto(Guid TeamId, string Name, string? Mobile, int ActiveTaskCount);

/// <summary>The outcome of a fill: the stored submission, and where it left the task.</summary>
public sealed record TaskFillResultDto(Guid SubmissionId, int VersionNo, bool IsReplay, string Status);
