namespace Tasks.Application.Common.Interfaces;

/// <summary>
/// Lays a task report out as a PDF. The handler gathers and resolves everything — labels, names,
/// image bytes — so the renderer only draws; that keeps the drawing library out of the application
/// layer and the report testable without it.
/// </summary>
public interface ITaskReportRenderer
{
    byte[] Render(TaskReport report);
}

/// <summary>
/// One task as printed. Every text is already in <see cref="Language"/>, except the lifecycle codes
/// (<see cref="Status"/>, <see cref="Priority"/>, <see cref="Source"/>, <see cref="ReturnReasonCode"/>),
/// which the renderer labels itself.
/// </summary>
public sealed record TaskReport
{
    public required string Language { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }

    public required string TaskNumber { get; init; }
    public required string Status { get; init; }
    public required string Priority { get; init; }
    public required string Source { get; init; }
    public string? Title { get; init; }
    public string? ExternalReference { get; init; }
    public string? Notes { get; init; }

    public required string TaskType { get; init; }
    public required string Form { get; init; }
    public required int FormVersionNo { get; init; }

    public required string Cluster { get; init; }
    public required string Cbu { get; init; }
    public required string Branch { get; init; }
    public required string OperationArea { get; init; }
    public required string Department { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public string? Address { get; init; }

    public DateTime CreatedAt { get; init; }
    public DateTime? DueDate { get; init; }
    public DateTime? CompletionDueDate { get; init; }
    public string? Team { get; init; }
    public DateTime? AssignedDate { get; init; }
    public DateTime? SubmittedDate { get; init; }
    public int SubmissionCount { get; init; }
    public DateTime? CompletedDate { get; init; }

    public string? ReturnReasonCode { get; init; }
    public string? ReturnReason { get; init; }
    public DateTime? ReturnedDate { get; init; }
    public int ReturnCount { get; init; }

    /// <summary>Who filled the answers below and when; null when the task was never filled.</summary>
    public TaskReportFill? LatestFill { get; init; }

    public IReadOnlyList<TaskReportAnswer> Answers { get; init; } = [];

    /// <summary>Every file the task's fills carry; only the latest fill's images have bytes.</summary>
    public IReadOnlyList<TaskReportFile> Files { get; init; } = [];
}

public sealed record TaskReportFill(string? FilledBy, DateTimeOffset? FilledAt, int VersionNo);

public sealed record TaskReportAnswer(string Label, string Value);

/// <summary>
/// One uploaded file. <see cref="Bytes"/> is read only for images of the latest fill, and stays null
/// when the bytes are gone or too large — the report then lists the file as not embedded rather than
/// leaving it out, which would read as "nothing was uploaded".
/// </summary>
public sealed record TaskReportFile(
    string FileName,
    string Field,
    string ContentType,
    long SizeBytes,
    DateTime CreatedAt,
    bool IsSignature,
    bool IsFromLatestFill,
    byte[]? Bytes)
{
    public bool IsImage => ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}
