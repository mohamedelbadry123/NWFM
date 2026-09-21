using NWFM.Shared.Domain;
using Tasks.Domain.Constants;

namespace Tasks.Domain.Entities;

/// <summary>
/// One team's hold on a task. A task keeps every assignment it has had — reassigning it supersedes
/// the live one rather than overwriting it — so the timeline can say who had the work, and when.
/// Table: <c>TK.TaskAssignments</c>.
/// </summary>
public sealed class TaskAssignment : Entity
{
    public const int NoteMaxLength = 1000;

    private TaskAssignment()
    {
    }

    internal TaskAssignment(
        Guid fieldTaskId,
        Guid teamId,
        string? assignedBy,
        DateTime? dueDate,
        string? note,
        DateTime utcNow)
    {
        FieldTaskId = fieldTaskId;
        TeamId = teamId;
        Status = TaskAssignmentStatuses.Pending;
        AssignedBy = assignedBy;
        AssignedDate = utcNow;
        DueDate = dueDate;
        Note = Clip(note);
        IsActive = true;
        CreatedAt = utcNow;
        UpdatedAt = utcNow;
    }

    public Guid FieldTaskId { get; private set; }

    /// <summary>The Auth team holding the work. A loose reference: Tasks never reads Auth's tables.</summary>
    public Guid TeamId { get; private set; }

    public string Status { get; private set; } = default!;
    public string? AssignedBy { get; private set; }
    public DateTime AssignedDate { get; private set; }
    public DateTime? DueDate { get; private set; }
    public DateTime? SubmittedDate { get; private set; }
    public string? Note { get; private set; }

    /// <summary>The live assignment. At most one per task.</summary>
    public bool IsActive { get; private set; }

    internal void MoveTo(string status, DateTime utcNow, bool stillActive = true)
    {
        Status = status;
        IsActive = stillActive;

        if (status == TaskAssignmentStatuses.Submitted)
        {
            SubmittedDate = utcNow;
        }

        SetUpdated(utcNow);
    }

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
