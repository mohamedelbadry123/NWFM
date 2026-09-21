using NWFM.Shared.Domain;
using NWFM.Shared.Exceptions;
using Tasks.Domain.Constants;

namespace Tasks.Domain.Entities;

/// <summary>
/// One piece of field work: a place, a territory, a deadline, and the form a crew fills there.
/// Modelled on the reference app's survey. Named <c>FieldTask</c> rather than <c>Task</c> so it never
/// competes with <see cref="System.Threading.Tasks.Task"/>. Table: <c>TK.Tasks</c>.
///
/// The form is pinned by <see cref="FormDefinitionId"/> and <see cref="FormVersionNo"/> when the task
/// is raised; its fills live in that form's own submission table, filed under this task's id.
/// </summary>
public sealed class FieldTask : Entity
{
    public const int TaskNumberMaxLength = 60;
    public const int TitleMaxLength = 250;
    public const int NotesMaxLength = 1000;
    public const int ExternalReferenceMaxLength = 100;
    public const int AddressMaxLength = 250;
    public const int OrgCodeMaxLength = 50;
    public const int ActorMaxLength = 256;
    public const int ReturnReasonMaxLength = 1000;

    private readonly List<TaskAssignment> _assignments = [];
    private readonly List<TaskStatusHistory> _history = [];

    private FieldTask()
    {
    }

    public string TaskNumber { get; private set; } = default!;
    public Guid TaskTypeId { get; private set; }

    /// <summary>The form filled for this task — its type's form when the task was raised.</summary>
    public Guid FormDefinitionId { get; private set; }

    /// <summary>The version pinned to this task. A later publish never changes it; <see cref="MigrateFormVersion"/> does, while unfilled.</summary>
    public int FormVersionNo { get; private set; }

    public string Source { get; private set; } = default!;
    public string Status { get; private set; } = default!;
    public string Priority { get; private set; } = default!;
    public string? Title { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>A reference the work carries elsewhere — a ticket, an asset, a work order.</summary>
    public string? ExternalReference { get; private set; }

    /// <summary>Anything an integration hands over that has no column of its own; JSON, <c>{}</c> by default.</summary>
    public string AdditionalDataJson { get; private set; } = "{}";

    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public string? Address { get; private set; }

    /// <summary>Territory. No cluster: it is derived from the CBU, as the reference survey does.</summary>
    public string? CbuCode { get; private set; }

    public string? BranchCode { get; private set; }
    public string? OperationAreaCode { get; private set; }
    public string? DepartmentCode { get; private set; }

    /// <summary>When the crew must have filled it.</summary>
    public DateTime? DueDate { get; private set; }

    /// <summary>When review must have finished.</summary>
    public DateTime? CompletionDueDate { get; private set; }

    public int? FillSlaHours { get; private set; }
    public int? CompletionSlaHours { get; private set; }

    public string? AssignedBy { get; private set; }
    public DateTime? AssignedDate { get; private set; }
    public DateTime? SubmittedDate { get; private set; }
    public string? LastFilledBy { get; private set; }
    public int SubmissionCount { get; private set; }

    /// <summary>The newest fill, in the pinned form's submission table.</summary>
    public Guid? LastSubmissionId { get; private set; }

    public string? CompletedBy { get; private set; }
    public DateTime? CompletedDate { get; private set; }
    public string? ReturnReasonCode { get; private set; }
    public string? ReturnReason { get; private set; }
    public string? ReturnedBy { get; private set; }
    public DateTime? ReturnedDate { get; private set; }
    public int ReturnCount { get; private set; }
    public string? ExpiredBy { get; private set; }
    public DateTime? ExpiredDate { get; private set; }

    /// <summary>False once the task has expired.</summary>
    public bool IsActive { get; private set; }

    public string? CreatedBy { get; private set; }
    public string? UpdatedBy { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<TaskAssignment> Assignments => _assignments.AsReadOnly();
    public IReadOnlyCollection<TaskStatusHistory> History => _history.AsReadOnly();

    /// <summary>The team holding the work now, if any.</summary>
    public TaskAssignment? ActiveAssignment => _assignments.FirstOrDefault(a => a.IsActive);

    public bool IsUnfilled => SubmissionCount == 0 && TaskStatuses.Unfilled.Contains(Status);

    public static FieldTask Create(FieldTaskDraft draft, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(draft.TaskNumber))
        {
            throw new DomainException("A task must have a number.");
        }

        if (draft.TaskTypeId == Guid.Empty || draft.FormDefinitionId == Guid.Empty)
        {
            throw new DomainException("A task must have a type and a form.");
        }

        if (draft.FormVersionNo <= 0)
        {
            throw new DomainException("A task must pin a published version of its form.");
        }

        if (!TaskSources.IsDefined(draft.Source))
        {
            throw new DomainException($"Unknown task source '{draft.Source}'.");
        }

        var task = new FieldTask
        {
            TaskNumber = draft.TaskNumber.Trim(),
            TaskTypeId = draft.TaskTypeId,
            FormDefinitionId = draft.FormDefinitionId,
            FormVersionNo = draft.FormVersionNo,
            Source = draft.Source,
            Status = TaskStatuses.Created,
            FillSlaHours = draft.FillSlaHours,
            CompletionSlaHours = draft.CompletionSlaHours,
            AdditionalDataJson = string.IsNullOrWhiteSpace(draft.AdditionalDataJson) ? "{}" : draft.AdditionalDataJson,
            IsActive = true,
            CreatedBy = Normalize(draft.CreatedBy),
            UpdatedBy = Normalize(draft.CreatedBy),
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };

        task.ApplyDetails(draft.Title, draft.Notes, draft.Priority, draft.ExternalReference, draft.DueDate, draft.CompletionDueDate);
        task.ApplyLocation(draft.Location);

        task._history.Add(new TaskStatusHistory(task.Id, null, TaskStatuses.Created, task.CreatedBy, null, utcNow));
        return task;
    }

    /// <summary>Title, notes, priority, reference and deadlines. Anything short of a closed task may be corrected.</summary>
    public void UpdateDetails(
        string? title,
        string? notes,
        string priority,
        string? externalReference,
        DateTime? dueDate,
        DateTime? completionDueDate,
        string? updatedBy,
        DateTime utcNow)
    {
        EnsureNotClosed("edited");
        ApplyDetails(title, notes, priority, externalReference, dueDate, completionDueDate);
        Touch(updatedBy, utcNow);
    }

    /// <summary>
    /// Moves the task. Only before it is filled: a fill records what was found at a place, and moving
    /// the place afterwards would make the answers describe somewhere they were not taken.
    /// </summary>
    public void Relocate(TaskLocation location, string? updatedBy, DateTime utcNow)
    {
        if (!IsUnfilled)
        {
            throw new DomainException($"A task can only be moved before it is filled (current: {Status}).");
        }

        ApplyLocation(location);
        AppendHistory(Status, updatedBy, "Location changed.", utcNow);
        Touch(updatedBy, utcNow);
    }

    /// <summary>
    /// Hands the task to a team. Only while unfilled: once a crew has filled it, the fill is theirs, and
    /// giving the work to another crew is done by returning it with a reassignment instead.
    /// </summary>
    public TaskAssignment Assign(
        Guid teamId,
        string? assignedBy,
        DateTime? dueDate,
        DateTime? completionDueDate,
        string? note,
        DateTime utcNow)
    {
        if (!IsUnfilled)
        {
            throw new DomainException($"A task can only be assigned before it is filled (current: {Status}).");
        }

        if (teamId == Guid.Empty)
        {
            throw new DomainException("A task must be assigned to a team.");
        }

        EnsureDeadlines(dueDate ?? DueDate, completionDueDate ?? CompletionDueDate);

        DueDate = dueDate ?? DueDate;
        CompletionDueDate = completionDueDate ?? CompletionDueDate;

        var assignment = HandTo(teamId, assignedBy, note, utcNow);

        AssignedBy = Normalize(assignedBy);
        AssignedDate = utcNow;
        MoveTo(TaskStatuses.Assigned, assignedBy, note, utcNow);
        return assignment;
    }

    /// <summary>
    /// Records a fill. Idempotent for the same submission, so a client retrying a fill whose first
    /// attempt stored the answers but lost the reply completes the task update instead of counting
    /// the fill twice.
    /// </summary>
    public void RecordFill(Guid submissionId, string? filledBy, DateTime utcNow)
    {
        if (LastSubmissionId == submissionId)
        {
            return;
        }

        EnsureNotClosed("filled");

        SubmissionCount++;
        LastSubmissionId = submissionId;
        SubmittedDate = utcNow;
        LastFilledBy = Normalize(filledBy);

        ActiveAssignment?.MoveTo(TaskAssignmentStatuses.Submitted, utcNow);

        if (Status == TaskStatuses.Submitted)
        {
            // A second fill before review: the answers changed, the status did not.
            AppendHistory(Status, filledBy, "Filled again.", utcNow);
        }
        else
        {
            MoveTo(TaskStatuses.Submitted, filledBy, null, utcNow);
        }

        Touch(filledBy, utcNow);
    }

    /// <summary>Approves a filled task.</summary>
    public void Complete(string? completedBy, string? note, DateTime utcNow)
    {
        EnsureReviewable("approved");

        CompletedBy = Normalize(completedBy);
        CompletedDate = utcNow;
        ReturnReasonCode = null;
        ReturnReason = null;

        ActiveAssignment?.MoveTo(TaskAssignmentStatuses.Approved, utcNow);
        MoveTo(TaskStatuses.Approved, completedBy, note, utcNow);
    }

    /// <summary>Sends a fill back for rework, optionally to another team.</summary>
    public void Return(
        string reasonCode,
        string reason,
        string? returnedBy,
        Guid? reassignToTeamId,
        DateTime utcNow)
    {
        EnsureReviewable("returned");

        if (!TaskReturnReasons.IsDefined(reasonCode))
        {
            throw new DomainException($"Unknown return reason '{reasonCode}'.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A return must say what needs doing.");
        }

        ReturnReasonCode = reasonCode;
        ReturnReason = Clip(reason, ReturnReasonMaxLength);
        ReturnedBy = Normalize(returnedBy);
        ReturnedDate = utcNow;
        ReturnCount++;

        var current = ActiveAssignment;

        if (reassignToTeamId is Guid teamId && teamId != current?.TeamId)
        {
            HandTo(teamId, returnedBy, ReturnReason, utcNow);
        }
        else
        {
            current?.MoveTo(TaskAssignmentStatuses.Returned, utcNow);
        }

        MoveTo(TaskStatuses.Returned, returnedBy, ReturnReason, utcNow);
    }

    /// <summary>Closes the task without it being approved — withdrawn, duplicated, no longer needed.</summary>
    public void Expire(string? expiredBy, string? note, DateTime utcNow)
    {
        EnsureNotClosed("expired");

        ExpiredBy = Normalize(expiredBy);
        ExpiredDate = utcNow;
        IsActive = false;

        ActiveAssignment?.MoveTo(TaskAssignmentStatuses.Expired, utcNow, stillActive: false);
        MoveTo(TaskStatuses.Expired, expiredBy, note, utcNow);
    }

    /// <summary>Re-pins an unfilled task to a newer published version of its form. Forward only.</summary>
    public void MigrateFormVersion(int versionNo, string? migratedBy, DateTime utcNow)
    {
        if (!IsUnfilled)
        {
            throw new DomainException($"A task can only move to a newer form version before it is filled (current: {Status}).");
        }

        if (versionNo <= FormVersionNo)
        {
            throw new DomainException($"The task is already on version {FormVersionNo}; it can only move forward.");
        }

        var from = FormVersionNo;
        FormVersionNo = versionNo;
        AppendHistory(Status, migratedBy, $"Form version {from} → {versionNo}.", utcNow);
        Touch(migratedBy, utcNow);
    }

    private TaskAssignment HandTo(Guid teamId, string? assignedBy, string? note, DateTime utcNow)
    {
        foreach (var live in _assignments.Where(a => a.IsActive))
        {
            live.MoveTo(TaskAssignmentStatuses.Reassigned, utcNow, stillActive: false);
        }

        var assignment = new TaskAssignment(Id, teamId, Normalize(assignedBy), DueDate, note, utcNow);
        _assignments.Add(assignment);
        return assignment;
    }

    private void ApplyDetails(
        string? title,
        string? notes,
        string priority,
        string? externalReference,
        DateTime? dueDate,
        DateTime? completionDueDate)
    {
        if (!TaskPriorities.IsDefined(priority))
        {
            throw new DomainException($"Unknown task priority '{priority}'.");
        }

        EnsureDeadlines(dueDate, completionDueDate);

        Title = Clip(title, TitleMaxLength);
        Notes = Clip(notes, NotesMaxLength);
        Priority = priority;
        ExternalReference = Clip(externalReference, ExternalReferenceMaxLength);
        DueDate = dueDate;
        CompletionDueDate = completionDueDate;
    }

    private void ApplyLocation(TaskLocation location)
    {
        if (location.Latitude is < -90 or > 90 || location.Longitude is < -180 or > 180
            || double.IsNaN(location.Latitude) || double.IsNaN(location.Longitude))
        {
            throw new DomainException("The task's coordinates are not a place on Earth.");
        }

        Latitude = location.Latitude;
        Longitude = location.Longitude;
        Address = Clip(location.Address, AddressMaxLength);
        CbuCode = Clip(location.CbuCode, OrgCodeMaxLength);
        BranchCode = Clip(location.BranchCode, OrgCodeMaxLength);
        OperationAreaCode = Clip(location.OperationAreaCode, OrgCodeMaxLength);
        DepartmentCode = Clip(location.DepartmentCode, OrgCodeMaxLength);
    }

    private static void EnsureDeadlines(DateTime? dueDate, DateTime? completionDueDate)
    {
        if (dueDate is not null && completionDueDate is not null && completionDueDate < dueDate)
        {
            throw new DomainException("Review cannot be due before the fill is.");
        }
    }

    private void EnsureNotClosed(string action)
    {
        if (TaskStatuses.IsClosed(Status))
        {
            throw new DomainException($"An {Status} task cannot be {action}.");
        }
    }

    private void EnsureReviewable(string action)
    {
        if (Status != TaskStatuses.Submitted)
        {
            throw new DomainException($"Only a filled task can be {action} (current: {Status}).");
        }
    }

    private void MoveTo(string status, string? changedBy, string? note, DateTime utcNow)
    {
        var from = Status;
        Status = status;
        _history.Add(new TaskStatusHistory(Id, from, status, Normalize(changedBy), note, utcNow));
        Touch(changedBy, utcNow);
    }

    private void AppendHistory(string status, string? changedBy, string? note, DateTime utcNow) =>
        _history.Add(new TaskStatusHistory(Id, status, status, Normalize(changedBy), note, utcNow));

    private void Touch(string? actor, DateTime utcNow)
    {
        UpdatedBy = Normalize(actor) ?? UpdatedBy;
        SetUpdated(utcNow);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Clip(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}

/// <summary>Where a task is: a point, the address it resolves to, and the territory it falls in.</summary>
public sealed record TaskLocation(
    double Latitude,
    double Longitude,
    string? Address,
    string? CbuCode,
    string? BranchCode,
    string? OperationAreaCode,
    string? DepartmentCode);

/// <summary>Everything a new task is raised with.</summary>
public sealed record FieldTaskDraft
{
    public required string TaskNumber { get; init; }
    public required Guid TaskTypeId { get; init; }
    public required Guid FormDefinitionId { get; init; }
    public required int FormVersionNo { get; init; }
    public string Source { get; init; } = TaskSources.Manual;
    public string? Title { get; init; }
    public string? Notes { get; init; }
    public string Priority { get; init; } = TaskPriorities.Normal;
    public string? ExternalReference { get; init; }
    public string? AdditionalDataJson { get; init; }
    public required TaskLocation Location { get; init; }
    public DateTime? DueDate { get; init; }
    public DateTime? CompletionDueDate { get; init; }
    public int? FillSlaHours { get; init; }
    public int? CompletionSlaHours { get; init; }
    public string? CreatedBy { get; init; }
}
