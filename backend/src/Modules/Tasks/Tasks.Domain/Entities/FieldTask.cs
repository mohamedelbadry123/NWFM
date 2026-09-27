using NWFM.Shared.Domain;
using NWFM.Shared.Exceptions;
using Tasks.Domain.Constants;

namespace Tasks.Domain.Entities;

/// <summary>
/// One piece of field work: a place, a territory, a deadline, and the forms a crew fills there.
/// Modelled on the reference app's survey. Named <c>FieldTask</c> rather than <c>Task</c> so it never
/// competes with <see cref="System.Threading.Tasks.Task"/>. Table: <c>Task.Tasks</c>.
///
/// Its forms (<see cref="Forms"/>) are pinned when the task is raised — the type's, plus any added to
/// this task — each at the version published then. One team fills them all; the task counts as filled
/// once every required form has a fill. Each form's fills live in its own submission table, filed
/// under this task's id.
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
    public const int FaIdMaxLength = 50;

    /// <summary>The type's forms plus those added to the one task.</summary>
    public const int MaxForms = 15;

    private readonly List<TaskForm> _forms = [];
    private readonly List<TaskAssignment> _assignments = [];
    private readonly List<TaskStatusHistory> _history = [];

    private FieldTask()
    {
    }

    public string TaskNumber { get; private set; } = default!;
    public Guid TaskTypeId { get; private set; }

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

    /// <summary>When any of its forms was last filled.</summary>
    public DateTime? SubmittedDate { get; private set; }

    public string? LastFilledBy { get; private set; }

    /// <summary>Fills across all its forms.</summary>
    public int SubmissionCount { get; private set; }

    /// <summary>The newest fill of any of its forms.</summary>
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

    /// <summary>
    /// The C2M field activity this task settles, when the work is C2M's. Approving the task closes that
    /// activity in C2M when the task's type says its form is the closing form.
    /// </summary>
    public string? FaId { get; private set; }

    /// <summary>WFM's ticket for the activity — C2M's <c>MOBId</c>.</summary>
    public long? WfmTicketId { get; private set; }

    /// <summary>Where closing the field activity in C2M stands (<see cref="C2mClosureStatuses"/>); null when it closes nothing.</summary>
    public string? C2mStatus { get; private set; }

    /// <summary>How many times the closure has been tried.</summary>
    public int C2mAttempts { get; private set; }

    /// <summary>When the closure was last tried. Also the background sender's claim on the task.</summary>
    public DateTime? C2mLastAttemptAt { get; private set; }

    /// <summary>False once the task has expired.</summary>
    public bool IsActive { get; private set; }

    public string? CreatedBy { get; private set; }
    public string? UpdatedBy { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<TaskForm> Forms => _forms.AsReadOnly();
    public IReadOnlyCollection<TaskAssignment> Assignments => _assignments.AsReadOnly();
    public IReadOnlyCollection<TaskStatusHistory> History => _history.AsReadOnly();

    /// <summary>The team holding the work now, if any.</summary>
    public TaskAssignment? ActiveAssignment => _assignments.FirstOrDefault(a => a.IsActive);

    public bool IsUnfilled => SubmissionCount == 0 && TaskStatuses.Unfilled.Contains(Status);

    /// <summary>The forms in the order a crew meets them.</summary>
    public IReadOnlyList<TaskForm> OrderedForms => _forms.OrderBy(f => f.SortOrder).ToList();

    public int RequiredFormCount => _forms.Count(f => f.IsRequired);

    public int FilledFormCount => _forms.Count(f => f.IsRequired && f.IsFilled);

    /// <summary>Whether every form the task waits for has a fill.</summary>
    public bool AllRequiredFormsFilled => _forms.Count > 0 && _forms.Where(f => f.IsRequired).All(f => f.IsFilled);

    /// <summary>The form whose answers close the C2M field activity: the one flagged, else the first.</summary>
    public TaskForm? C2mClosingForm => _forms
        .OrderByDescending(f => f.IsC2mClosingForm)
        .ThenBy(f => f.SortOrder)
        .FirstOrDefault();

    /// <summary>One of the task's forms; null when it is not on the task.</summary>
    public TaskForm? FormOf(Guid formDefinitionId) => _forms.FirstOrDefault(f => f.FormDefinitionId == formDefinitionId);

    public static FieldTask Create(FieldTaskDraft draft, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(draft.TaskNumber))
        {
            throw new DomainException("A task must have a number.");
        }

        if (draft.TaskTypeId == Guid.Empty)
        {
            throw new DomainException("A task must have a type.");
        }

        if (draft.Forms is null || draft.Forms.Count == 0 || draft.Forms.Any(f => f.FormDefinitionId == Guid.Empty))
        {
            throw new DomainException("A task must have at least one form.");
        }

        if (draft.Forms.Any(f => f.VersionNo <= 0))
        {
            throw new DomainException("A task must pin a published version of each of its forms.");
        }

        if (draft.Forms.Select(f => f.FormDefinitionId).Distinct().Count() != draft.Forms.Count)
        {
            throw new DomainException("A task cannot carry the same form twice.");
        }

        if (draft.Forms.Count > MaxForms)
        {
            throw new DomainException($"A task can carry at most {MaxForms} forms.");
        }

        if (!TaskSources.IsDefined(draft.Source))
        {
            throw new DomainException($"Unknown task source '{draft.Source}'.");
        }

        var task = new FieldTask
        {
            TaskNumber = draft.TaskNumber.Trim(),
            TaskTypeId = draft.TaskTypeId,
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
        task.ApplyFieldActivity(draft.FaId, draft.WfmTicketId);

        for (var i = 0; i < draft.Forms.Count; i++)
        {
            task._forms.Add(new TaskForm(task.Id, draft.Forms[i], i, task.CreatedBy, utcNow));
        }

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
    /// Names the C2M field activity the task settles. Not once it is approved: the closure already
    /// sent, or queued, refers to the activity it had then.
    /// </summary>
    public void SetFieldActivity(string? faId, long? wfmTicketId, string? updatedBy, DateTime utcNow)
    {
        EnsureNotClosed("given a field activity");
        ApplyFieldActivity(faId, wfmTicketId);
        Touch(updatedBy, utcNow);
    }

    /// <summary>
    /// Records one attempt at closing the field activity in C2M, and where that leaves the closure.
    /// Kept apart from <see cref="Touch"/>: a background retry is not an edit anyone made.
    /// </summary>
    public void RecordC2mAttempt(string closureStatus, DateTime utcNow)
    {
        if (!C2mClosureStatuses.All.Contains(closureStatus))
        {
            throw new DomainException($"Unknown C2M closure status '{closureStatus}'.");
        }

        C2mStatus = closureStatus;
        C2mAttempts++;
        C2mLastAttemptAt = utcNow;
    }

    /// <summary>
    /// Queues the closure for the background sender — an approval that did not wait for C2M, or a
    /// person asking for a refused closure to be sent again.
    /// </summary>
    public void QueueC2mClosure()
    {
        if (string.IsNullOrWhiteSpace(FaId))
        {
            throw new DomainException("A task with no field activity has nothing to close in C2M.");
        }

        C2mStatus = C2mClosureStatuses.Pending;
    }

    /// <summary>
    /// Claims the task for one background attempt. Saved before the call, so with the row version a
    /// second sender working the same queue loses the race instead of sending the closure twice.
    /// </summary>
    public void ClaimC2mAttempt(DateTime utcNow) => C2mLastAttemptAt = utcNow;

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
    /// Records a fill of one of the task's forms, with the computed columns it worked out. The task
    /// is SUBMITTED once every required form has a fill, and IN_PROGRESS while some still wait.
    /// Idempotent for the same submission, so a client retrying a fill whose first attempt stored the
    /// answers but lost the reply completes the task update instead of counting the fill twice.
    /// </summary>
    public void RecordFill(
        Guid formDefinitionId,
        Guid submissionId,
        string? filledBy,
        DateTime utcNow,
        IReadOnlyList<TaskComputedValueDraft>? computed = null)
    {
        var form = FormOf(formDefinitionId)
            ?? throw new DomainException("That form is not one of this task's forms.");

        if (form.LastSubmissionId == submissionId)
        {
            return;
        }

        EnsureNotClosed("filled");

        var actor = Normalize(filledBy);
        form.RecordFill(submissionId, actor, utcNow);
        form.ReplaceComputed(computed ?? [], submissionId, utcNow);

        SubmissionCount++;
        LastSubmissionId = submissionId;
        SubmittedDate = utcNow;
        LastFilledBy = actor;

        if (AllRequiredFormsFilled)
        {
            ActiveAssignment?.MoveTo(TaskAssignmentStatuses.Submitted, utcNow);

            if (Status == TaskStatuses.Submitted)
            {
                // A fill again before review: the answers changed, the status did not.
                AppendHistory(Status, filledBy, $"{FormLabel(form)}Filled again.", utcNow);
            }
            else
            {
                MoveTo(TaskStatuses.Submitted, filledBy, _forms.Count > 1 ? $"All {_forms.Count} forms filled." : null, utcNow);
            }
        }
        else
        {
            ActiveAssignment?.MoveTo(TaskAssignmentStatuses.InProgress, utcNow);
            var note = $"Form {form.SortOrder + 1} filled ({FilledFormCount} of {RequiredFormCount}).";

            if (Status == TaskStatuses.InProgress)
            {
                AppendHistory(Status, filledBy, note, utcNow);
            }
            else
            {
                MoveTo(TaskStatuses.InProgress, filledBy, note, utcNow);
            }
        }

        Touch(filledBy, utcNow);
    }

    /// <summary>
    /// Adds a form to this task alone. Not to one waiting for review — its fills were judged complete
    /// without it; return the task first — and not to a closed one.
    /// </summary>
    public TaskForm AttachForm(Guid formDefinitionId, int versionNo, string? addedBy, DateTime utcNow)
    {
        EnsureNotClosed("given another form");

        if (Status == TaskStatuses.Submitted)
        {
            throw new DomainException("A task waiting for review cannot take another form; return it first.");
        }

        if (formDefinitionId == Guid.Empty || versionNo <= 0)
        {
            throw new DomainException("A form added to a task must have a published version.");
        }

        if (FormOf(formDefinitionId) is not null)
        {
            throw new DomainException("The task already carries that form.");
        }

        if (_forms.Count >= MaxForms)
        {
            throw new DomainException($"A task can carry at most {MaxForms} forms.");
        }

        var sortOrder = _forms.Count == 0 ? 0 : _forms.Max(f => f.SortOrder) + 1;
        var form = new TaskForm(Id, new TaskFormDraft(formDefinitionId, versionNo, TaskFormSources.Extra), sortOrder, Normalize(addedBy), utcNow);
        _forms.Add(form);

        AppendHistory(Status, addedBy, $"Form {sortOrder + 1} added.", utcNow);
        Touch(addedBy, utcNow);
        return form;
    }

    /// <summary>
    /// Removes a form added to this task. The type's forms stay: they are what the type is. Only
    /// before any fill, so nothing already recorded is left pointing at a form the task dropped.
    /// </summary>
    public void DetachForm(Guid formDefinitionId, string? removedBy, DateTime utcNow)
    {
        var form = FormOf(formDefinitionId)
            ?? throw new DomainException("That form is not one of this task's forms.");

        if (form.Source != TaskFormSources.Extra)
        {
            throw new DomainException("Only a form added to the task can be removed; its type's forms stay.");
        }

        if (!IsUnfilled)
        {
            throw new DomainException($"A form can only be removed before the task is filled (current: {Status}).");
        }

        _forms.Remove(form);
        AppendHistory(Status, removedBy, $"Form {form.SortOrder + 1} removed.", utcNow);
        Touch(removedBy, utcNow);
    }

    /// <summary>Approves a filled task.</summary>
    public void Complete(string? completedBy, string? note, DateTime utcNow)
    {
        EnsureReviewable("approved");

        if (!AllRequiredFormsFilled)
        {
            throw new DomainException("Every form of the task must be filled before it can be approved.");
        }

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

    /// <summary>
    /// Re-pins one of the task's forms to a newer published version. Forward only, and only while that
    /// form is unfilled: a fill answered the version it was taken against.
    /// </summary>
    public void MigrateFormVersion(Guid formDefinitionId, int versionNo, string? migratedBy, DateTime utcNow)
    {
        var form = FormOf(formDefinitionId)
            ?? throw new DomainException("That form is not one of this task's forms.");

        EnsureNotClosed("moved to a newer form version");

        if (form.IsFilled)
        {
            throw new DomainException($"A form can only move to a newer version before it is filled (current: {Status}).");
        }

        if (versionNo <= form.FormVersionNo)
        {
            throw new DomainException($"The form is already on version {form.FormVersionNo}; it can only move forward.");
        }

        var from = form.FormVersionNo;
        form.MigrateVersion(versionNo, utcNow);
        AppendHistory(Status, migratedBy, $"{FormLabel(form)}Form version {from} → {versionNo}.", utcNow);
        Touch(migratedBy, utcNow);
    }

    /// <summary>Names the form in a timeline note, when the task has more than one.</summary>
    private string FormLabel(TaskForm form) => _forms.Count > 1 ? $"Form {form.SortOrder + 1}: " : string.Empty;

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

    private void ApplyFieldActivity(string? faId, long? wfmTicketId)
    {
        if (wfmTicketId is <= 0)
        {
            throw new DomainException("A WFM ticket id must be a positive number.");
        }

        FaId = Clip(faId, FaIdMaxLength);
        WfmTicketId = wfmTicketId;
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

    /// <summary>The forms to pin, in order: the type's, then any added to this task.</summary>
    public required IReadOnlyList<TaskFormDraft> Forms { get; init; }

    public string Source { get; init; } = TaskSources.Manual;
    public string? Title { get; init; }
    public string? Notes { get; init; }
    public string Priority { get; init; } = TaskPriorities.Normal;
    public string? ExternalReference { get; init; }
    public string? FaId { get; init; }
    public long? WfmTicketId { get; init; }
    public string? AdditionalDataJson { get; init; }
    public required TaskLocation Location { get; init; }
    public DateTime? DueDate { get; init; }
    public DateTime? CompletionDueDate { get; init; }
    public int? FillSlaHours { get; init; }
    public int? CompletionSlaHours { get; init; }
    public string? CreatedBy { get; init; }
}
