using NWFM.Shared.Domain;

namespace Tasks.Domain.Entities;

/// <summary>
/// One line of a task's timeline. Append-only. A change that is not a status change — a refill, a
/// relocation, a form re-pin — is logged with the same status on both sides, so the timeline shows
/// everything that happened, not only the transitions. Table: <c>TK.TaskStatusHistory</c>.
/// </summary>
public sealed class TaskStatusHistory : Entity, IImmutableEntity
{
    public const int NoteMaxLength = 1000;

    private TaskStatusHistory()
    {
    }

    internal TaskStatusHistory(
        Guid fieldTaskId,
        string? fromStatus,
        string toStatus,
        string? changedBy,
        string? note,
        DateTime utcNow)
    {
        FieldTaskId = fieldTaskId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ChangedBy = changedBy;
        ChangedDate = utcNow;
        Note = Clip(note);
        CreatedAt = utcNow;
        UpdatedAt = utcNow;
    }

    public Guid FieldTaskId { get; private set; }

    /// <summary>Null on the row that records the task being raised.</summary>
    public string? FromStatus { get; private set; }

    public string ToStatus { get; private set; } = default!;
    public string? ChangedBy { get; private set; }
    public DateTime ChangedDate { get; private set; }
    public string? Note { get; private set; }

    private static string? Clip(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        var trimmed = note.Trim();
        return trimmed.Length <= NoteMaxLength ? trimmed : trimmed[..NoteMaxLength];
    }
}
