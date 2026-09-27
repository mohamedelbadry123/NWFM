using NWFM.Shared.Domain;

namespace Tasks.Domain.Entities;

/// <summary>
/// One computed column of one of a task's forms, as worked out of that form's latest fill — what the
/// task grid shows and sorts on. Worked out by the form engine at fill time, through the version the
/// fill answered, and replaced by the next fill of the form; never recomputed when the form is
/// republished. Table: <c>Task.TaskComputedValues</c>.
/// </summary>
public sealed class TaskComputedValue : Entity
{
    public const int KeyMaxLength = 64;
    public const int OutputTypeMaxLength = 10;
    public const int TextMaxLength = 400;

    private TaskComputedValue()
    {
    }

    internal TaskComputedValue(TaskForm form, TaskComputedValueDraft draft, Guid submissionId, DateTime utcNow)
    {
        TaskFormId = form.Id;
        FieldTaskId = form.FieldTaskId;
        FormDefinitionId = form.FormDefinitionId;
        Key = draft.Key;
        CreatedAt = utcNow;
        Apply(draft, form.FormVersionNo, submissionId, utcNow);
    }

    public Guid TaskFormId { get; private set; }

    /// <summary>Copied from the form, so the grid reads a page's values without joining through it.</summary>
    public Guid FieldTaskId { get; private set; }

    public Guid FormDefinitionId { get; private set; }

    /// <summary>The column's key within its form.</summary>
    public string Key { get; private set; } = default!;

    /// <summary><c>text</c> or <c>number</c>.</summary>
    public string OutputType { get; private set; } = default!;

    /// <summary>The value as shown; a number's rendering too.</summary>
    public string? ValueText { get; private set; }

    /// <summary>Set for a number column, so the grid sorts it as a number.</summary>
    public decimal? ValueNumber { get; private set; }

    /// <summary>The version the fill answered, whose rules produced the value.</summary>
    public int FormVersionNo { get; private set; }

    public Guid SubmissionId { get; private set; }

    public DateTime ComputedAt { get; private set; }

    internal void Apply(TaskComputedValueDraft draft, int formVersionNo, Guid submissionId, DateTime utcNow)
    {
        OutputType = draft.OutputType;
        ValueNumber = draft.Number;
        ValueText = string.IsNullOrEmpty(draft.Text)
            ? null
            : draft.Text.Length <= TextMaxLength ? draft.Text : draft.Text[..TextMaxLength];
        FormVersionNo = formVersionNo;
        SubmissionId = submissionId;
        ComputedAt = utcNow;
        SetUpdated(utcNow);
    }
}

/// <summary>A computed column's value for a fill, as the form engine worked it out.</summary>
public sealed record TaskComputedValueDraft(string Key, string OutputType, decimal? Number, string? Text);
