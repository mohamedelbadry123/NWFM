using NWFM.Shared.Domain;
using Tasks.Domain.Constants;

namespace Tasks.Domain.Entities;

/// <summary>
/// One form pinned to a task, at the version published when it was pinned. A task is filled only
/// once every required form has a fill; each form's fills live in that form's own submission table,
/// filed under the task's id. Table: <c>Task.TaskForms</c>.
/// </summary>
public sealed class TaskForm : Entity
{
    private readonly List<TaskComputedValue> _computedValues = [];

    private TaskForm()
    {
    }

    internal TaskForm(Guid fieldTaskId, TaskFormDraft draft, int sortOrder, string? addedBy, DateTime utcNow)
    {
        FieldTaskId = fieldTaskId;
        FormDefinitionId = draft.FormDefinitionId;
        FormVersionNo = draft.VersionNo;
        SortOrder = sortOrder;
        Source = draft.Source;
        IsRequired = true;
        IsC2mClosingForm = draft.IsC2mClosingForm;
        AddedBy = addedBy;
        CreatedAt = utcNow;
        UpdatedAt = utcNow;
    }

    public Guid FieldTaskId { get; private set; }

    /// <summary>A loose reference: Tasks never reads FormEngine's tables.</summary>
    public Guid FormDefinitionId { get; private set; }

    /// <summary>The version pinned. A later publish never changes it; a migration does, while this form is unfilled.</summary>
    public int FormVersionNo { get; private set; }

    /// <summary>Zero-based position; the type's forms first, in the type's order, then any added to the task.</summary>
    public int SortOrder { get; private set; }

    /// <summary><see cref="TaskFormSources"/>: from the task's type, or added to this task alone.</summary>
    public string Source { get; private set; } = default!;

    /// <summary>Whether the task waits for this form before it counts as filled. Every form is, today.</summary>
    public bool IsRequired { get; private set; }

    /// <summary>The form whose answers close the task's C2M field activity.</summary>
    public bool IsC2mClosingForm { get; private set; }

    public int SubmissionCount { get; private set; }

    /// <summary>The newest fill, in this form's submission table.</summary>
    public Guid? LastSubmissionId { get; private set; }

    public DateTime? SubmittedDate { get; private set; }
    public string? LastFilledBy { get; private set; }
    public string? AddedBy { get; private set; }

    public bool IsFilled => SubmissionCount > 0;

    /// <summary>The form's computed columns, as its latest fill worked them out.</summary>
    public IReadOnlyCollection<TaskComputedValue> ComputedValues => _computedValues.AsReadOnly();

    /// <summary>Records a fill; false when it is the same submission recorded already (a client retry).</summary>
    internal bool RecordFill(Guid submissionId, string? filledBy, DateTime utcNow)
    {
        if (LastSubmissionId == submissionId)
        {
            return false;
        }

        SubmissionCount++;
        LastSubmissionId = submissionId;
        SubmittedDate = utcNow;
        LastFilledBy = filledBy;
        SetUpdated(utcNow);
        return true;
    }

    /// <summary>
    /// Replaces the computed values with the latest fill's. Columns the fill still yields keep their
    /// rows; a column the version no longer declares is dropped.
    /// </summary>
    internal void ReplaceComputed(IReadOnlyList<TaskComputedValueDraft> drafts, Guid submissionId, DateTime utcNow)
    {
        var keys = new HashSet<string>(drafts.Select(d => d.Key), StringComparer.Ordinal);
        _computedValues.RemoveAll(v => !keys.Contains(v.Key));

        foreach (var draft in drafts.DistinctBy(d => d.Key))
        {
            var existing = _computedValues.FirstOrDefault(v => v.Key == draft.Key);

            if (existing is null)
            {
                _computedValues.Add(new TaskComputedValue(this, draft, submissionId, utcNow));
            }
            else
            {
                existing.Apply(draft, FormVersionNo, submissionId, utcNow);
            }
        }
    }

    internal void MigrateVersion(int versionNo, DateTime utcNow)
    {
        FormVersionNo = versionNo;
        SetUpdated(utcNow);
    }
}

/// <summary>A form to pin to a task: which, at what version, and why it is there.</summary>
public sealed record TaskFormDraft(Guid FormDefinitionId, int VersionNo, string Source, bool IsC2mClosingForm = false);
