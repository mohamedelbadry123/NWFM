using NWFM.Shared.Integration.Forms;

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

    /// <summary>The forms the task is filled with, in order.</summary>
    public IReadOnlyList<TaskFormDto> Forms { get; init; } = [];

    /// <summary>How many forms the task waits for, and how many of those have a fill.</summary>
    public int RequiredFormCount { get; init; }

    public int FilledFormCount { get; init; }

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

    /// <summary>The C2M field activity the task settles, if any.</summary>
    public string? FaId { get; init; }

    /// <summary>WFM's ticket for the activity — C2M's MOBId.</summary>
    public long? WfmTicketId { get; init; }

    /// <summary>Where closing it in C2M stands; null when the task closes nothing in C2M.</summary>
    public string? C2mStatus { get; init; }

    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    /// <summary>
    /// The computed columns its forms' latest fills worked out, keyed by <see cref="TaskComputedColumnDto.Id"/>.
    /// Filled on the worklist only; a column the task's forms do not declare is simply absent.
    /// </summary>
    public IReadOnlyList<TaskComputedCellDto> ComputedValues { get; internal set; } = [];
}

/// <summary>One task's value of one computed column: its text, and its number when the column is a number.</summary>
public sealed record TaskComputedCellDto(string ColumnId, string? Text, decimal? Number);

/// <summary>
/// A computed column the task grid can show: one form's key. <see cref="Id"/> is
/// <c>{formId}:{key}</c> — what a cell names and what the grid sorts by (<c>computed:{Id}</c>).
/// </summary>
public sealed record TaskComputedColumnDto(
    string Id,
    Guid FormDefinitionId,
    string? FormCode,
    string? FormNameEn,
    string? FormNameAr,
    string Key,
    string? LabelEn,
    string? LabelAr,
    string OutputType,
    bool ShowInTaskGrid);

/// <summary>How a computed column is named in a cell and in a sort field.</summary>
public static class TaskComputedColumnIds
{
    public const string SortPrefix = "computed:";

    public static string Of(Guid formDefinitionId, string key) => $"{formDefinitionId:D}:{key}";

    /// <summary>Reads <c>{formId}:{key}</c>, or a sort field <c>computed:{formId}:{key}</c>.</summary>
    public static bool TryParse(string? value, out Guid formDefinitionId, out string key)
    {
        formDefinitionId = Guid.Empty;
        key = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.StartsWith(SortPrefix, StringComparison.OrdinalIgnoreCase) ? value[SortPrefix.Length..] : value;
        var separator = text.IndexOf(':');

        if (separator <= 0 || separator == text.Length - 1 || !Guid.TryParse(text[..separator], out formDefinitionId))
        {
            return false;
        }

        key = text[(separator + 1)..];
        return true;
    }
}

/// <summary>
/// One form of a task. <see cref="CurrentVersionNo"/> is the form's current published version — ahead
/// of <see cref="VersionNo"/> when a newer one was published since. <see cref="SchemaJson"/> is the
/// pinned version's form-builder document, sent only when a single task is read.
/// </summary>
public sealed class TaskFormDto
{
    public Guid FormDefinitionId { get; init; }
    public string? Code { get; init; }
    public string? NameEn { get; init; }
    public string? NameAr { get; init; }
    public int VersionNo { get; init; }
    public int? CurrentVersionNo { get; init; }
    public int SortOrder { get; init; }
    public string Source { get; init; } = default!;
    public bool IsRequired { get; init; }
    public bool IsC2mClosingForm { get; init; }
    public int SubmissionCount { get; init; }
    public DateTime? SubmittedDate { get; init; }
    public string? LastFilledBy { get; init; }
    public string? SchemaJson { get; init; }
}

/// <summary>A task opened on its own: everything on the row, its assignments, and the forms it is filled with.</summary>
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
    public int C2mAttempts { get; init; }
    public DateTime? C2mLastAttemptAt { get; init; }

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

/// <summary>
/// One fill of one of the task's forms. <see cref="Answers"/> is the stored row, which the web
/// renderer reads back into the form; <see cref="Display"/> is the same answers labelled and
/// rendered, in the form's order.
/// </summary>
public sealed record TaskFillDto(
    Guid FormDefinitionId,
    Guid SubmissionId,
    int VersionNo,
    string? SubmittedBy,
    string? SubmittedByName,
    DateTimeOffset? SubmittedDate,
    IReadOnlyDictionary<string, object?> Answers,
    IReadOnlyList<FormAnswerView> Display);

public sealed record TaskFileDto(
    Guid FormDefinitionId,
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

/// <summary>
/// The outcome of a fill: the stored submission, and where it left the task — how many of its forms
/// now have a fill, of how many it waits for.
/// </summary>
public sealed record TaskFillResultDto(
    Guid SubmissionId,
    Guid FormDefinitionId,
    int VersionNo,
    bool IsReplay,
    string Status,
    int FilledFormCount,
    int RequiredFormCount);
