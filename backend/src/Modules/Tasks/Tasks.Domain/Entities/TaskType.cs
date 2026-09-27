using NWFM.Shared.Domain;
using NWFM.Shared.Exceptions;

namespace Tasks.Domain.Entities;

/// <summary>
/// A kind of field work, and the forms its tasks are filled with. Different types collect different
/// inputs; a task pins each of its type's forms, at the version current when the task was raised, so
/// changing the type's forms later never changes a task already in the field. Table: <c>Task.TaskTypes</c>.
/// </summary>
public sealed class TaskType : Entity
{
    public const int CodeMaxLength = 50;
    public const int NameMaxLength = 250;
    public const int DescriptionMaxLength = 1000;
    public const int DepartmentCodeMaxLength = 50;
    public const int ActorMaxLength = 256;

    /// <summary>A year: past that an SLA is a typo, not a target.</summary>
    public const int MaxSlaHours = 24 * 366;

    /// <summary>More than this is a survey, not a visit; split it into types.</summary>
    public const int MaxForms = 10;

    private readonly List<TaskTypeForm> _forms = [];

    private TaskType()
    {
    }

    public string Code { get; private set; } = default!;
    public string NameEn { get; private set; } = default!;
    public string NameAr { get; private set; } = default!;
    public string? DescriptionEn { get; private set; }
    public string? DescriptionAr { get; private set; }


    /// <summary>The department whose work this is (<c>Auth.LKP_DEPARTMENT.Code</c>); a new task inherits it.</summary>
    public string? DepartmentCode { get; private set; }

    /// <summary>Hours a crew has to fill a task once it is assigned; seeds the fill due date.</summary>
    public int? FillSlaHours { get; private set; }

    /// <summary>Hours after the fill deadline for review to finish; seeds the completion due date.</summary>
    public int? CompletionSlaHours { get; private set; }

    /// <summary>
    /// Whether this type's form is the closing form for C2M field activities: approving one of its
    /// tasks that carries an FA id closes that activity in C2M.
    /// </summary>
    public bool ClosesC2mActivity { get; private set; }

    public bool IsActive { get; private set; }
    public string? CreatedBy { get; private set; }
    public string? UpdatedBy { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>The forms its tasks are filled with, in order. Never empty.</summary>
    public IReadOnlyCollection<TaskTypeForm> Forms => _forms.AsReadOnly();

    /// <summary>The form ids in order — what a task raised now pins.</summary>
    public IReadOnlyList<Guid> FormIds => _forms.OrderBy(f => f.SortOrder).Select(f => f.FormDefinitionId).ToList();

    public static TaskType Create(
        string code,
        string nameEn,
        string nameAr,
        string? descriptionEn,
        string? descriptionAr,
        IReadOnlyList<Guid> formDefinitionIds,
        Guid? c2mClosingFormId,
        string? departmentCode,
        int? fillSlaHours,
        int? completionSlaHours,
        string? createdBy,
        DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainException("A task type must have a code.");
        }

        var type = new TaskType
        {
            Code = code.Trim(),
            IsActive = true,
            CreatedBy = Normalize(createdBy),
            CreatedAt = utcNow,
        };

        type.Apply(nameEn, nameAr, descriptionEn, descriptionAr, departmentCode, fillSlaHours, completionSlaHours);
        type.SetForms(formDefinitionIds, c2mClosingFormId, utcNow);
        type.Touch(createdBy, utcNow);
        return type;
    }

    public void Update(
        string nameEn,
        string nameAr,
        string? descriptionEn,
        string? descriptionAr,
        IReadOnlyList<Guid> formDefinitionIds,
        Guid? c2mClosingFormId,
        string? departmentCode,
        int? fillSlaHours,
        int? completionSlaHours,
        string? updatedBy,
        DateTime utcNow)
    {
        Apply(nameEn, nameAr, descriptionEn, descriptionAr, departmentCode, fillSlaHours, completionSlaHours);
        SetForms(formDefinitionIds, c2mClosingFormId, utcNow);
        Touch(updatedBy, utcNow);
    }

    public void SetClosesC2mActivity(bool closes, string? updatedBy, DateTime utcNow)
    {
        ClosesC2mActivity = closes;
        Touch(updatedBy, utcNow);
    }

    public void SetActive(bool isActive, string? updatedBy, DateTime utcNow)
    {
        IsActive = isActive;
        Touch(updatedBy, utcNow);
    }

    /// <summary>
    /// Replaces the type's forms, in the order given. Forms kept keep their rows; the closing form is
    /// the one named, or the first when none is.
    /// </summary>
    private void SetForms(IReadOnlyList<Guid> formDefinitionIds, Guid? c2mClosingFormId, DateTime utcNow)
    {
        if (formDefinitionIds is null || formDefinitionIds.Count == 0 || formDefinitionIds.Any(id => id == Guid.Empty))
        {
            throw new DomainException("A task type must name the forms its tasks are filled with.");
        }

        if (formDefinitionIds.Distinct().Count() != formDefinitionIds.Count)
        {
            throw new DomainException("A task type cannot list the same form twice.");
        }

        if (formDefinitionIds.Count > MaxForms)
        {
            throw new DomainException($"A task type can have at most {MaxForms} forms.");
        }

        if (c2mClosingFormId is Guid closing && !formDefinitionIds.Contains(closing))
        {
            throw new DomainException("The C2M closing form must be one of the type's forms.");
        }

        var closingFormId = c2mClosingFormId ?? formDefinitionIds[0];

        _forms.RemoveAll(f => !formDefinitionIds.Contains(f.FormDefinitionId));

        for (var i = 0; i < formDefinitionIds.Count; i++)
        {
            var formId = formDefinitionIds[i];
            var existing = _forms.FirstOrDefault(f => f.FormDefinitionId == formId);

            if (existing is null)
            {
                _forms.Add(new TaskTypeForm(Id, formId, i, formId == closingFormId, utcNow));
            }
            else
            {
                existing.Place(i, formId == closingFormId, utcNow);
            }
        }
    }

    private void Apply(
        string nameEn,
        string nameAr,
        string? descriptionEn,
        string? descriptionAr,
        string? departmentCode,
        int? fillSlaHours,
        int? completionSlaHours)
    {
        if (string.IsNullOrWhiteSpace(nameEn) || string.IsNullOrWhiteSpace(nameAr))
        {
            throw new DomainException("A task type must have an English and an Arabic name.");
        }


        EnsureSla(fillSlaHours, "fill");
        EnsureSla(completionSlaHours, "completion");

        NameEn = nameEn.Trim();
        NameAr = nameAr.Trim();
        DescriptionEn = Normalize(descriptionEn);
        DescriptionAr = Normalize(descriptionAr);
        DepartmentCode = Normalize(departmentCode);
        FillSlaHours = fillSlaHours;
        CompletionSlaHours = completionSlaHours;
    }

    private static void EnsureSla(int? hours, string which)
    {
        if (hours is not null && (hours <= 0 || hours > MaxSlaHours))
        {
            throw new DomainException($"The {which} SLA must be between 1 and {MaxSlaHours} hours.");
        }
    }

    private void Touch(string? actor, DateTime utcNow)
    {
        UpdatedBy = Normalize(actor) ?? UpdatedBy;
        SetUpdated(utcNow);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
