using NWFM.Shared.Domain;

namespace Tasks.Domain.Entities;

/// <summary>
/// One of the forms a task type's tasks are filled with, in the order a crew meets them. Every task
/// raised from the type pins each of them. Table: <c>Task.TaskTypeForms</c>.
/// </summary>
public sealed class TaskTypeForm : Entity
{
    private TaskTypeForm()
    {
    }

    internal TaskTypeForm(Guid taskTypeId, Guid formDefinitionId, int sortOrder, bool isC2mClosingForm, DateTime utcNow)
    {
        TaskTypeId = taskTypeId;
        FormDefinitionId = formDefinitionId;
        SortOrder = sortOrder;
        IsC2mClosingForm = isC2mClosingForm;
        CreatedAt = utcNow;
        UpdatedAt = utcNow;
    }

    public Guid TaskTypeId { get; private set; }

    /// <summary>A loose reference: Tasks never reads FormEngine's tables.</summary>
    public Guid FormDefinitionId { get; private set; }

    /// <summary>Zero-based position; the first form is the type's primary one.</summary>
    public int SortOrder { get; private set; }

    /// <summary>
    /// The form whose answers close the C2M field activity, when the type closes one. At most one per
    /// type; carried onto each task the type raises.
    /// </summary>
    public bool IsC2mClosingForm { get; private set; }

    internal void Place(int sortOrder, bool isC2mClosingForm, DateTime utcNow)
    {
        if (SortOrder == sortOrder && IsC2mClosingForm == isC2mClosingForm)
        {
            return;
        }

        SortOrder = sortOrder;
        IsC2mClosingForm = isC2mClosingForm;
        SetUpdated(utcNow);
    }
}
