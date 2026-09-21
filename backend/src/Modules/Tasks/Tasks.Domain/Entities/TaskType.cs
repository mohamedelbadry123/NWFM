using NWFM.Shared.Domain;
using NWFM.Shared.Exceptions;

namespace Tasks.Domain.Entities;

/// <summary>
/// A kind of field work, and the form its tasks are filled with. Different types collect different
/// inputs; a task pins its type's form, at the version current when the task was raised, so changing
/// the type's form later never changes a task already in the field. Table: <c>Task.TaskTypes</c>.
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

    private TaskType()
    {
    }

    public string Code { get; private set; } = default!;
    public string NameEn { get; private set; } = default!;
    public string NameAr { get; private set; } = default!;
    public string? DescriptionEn { get; private set; }
    public string? DescriptionAr { get; private set; }

    /// <summary>The form its tasks are filled with. A loose reference: Tasks never reads FormEngine's tables.</summary>
    public Guid FormDefinitionId { get; private set; }

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

    public static TaskType Create(
        string code,
        string nameEn,
        string nameAr,
        string? descriptionEn,
        string? descriptionAr,
        Guid formDefinitionId,
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

        type.Apply(nameEn, nameAr, descriptionEn, descriptionAr, formDefinitionId, departmentCode, fillSlaHours, completionSlaHours);
        type.Touch(createdBy, utcNow);
        return type;
    }

    public void Update(
        string nameEn,
        string nameAr,
        string? descriptionEn,
        string? descriptionAr,
        Guid formDefinitionId,
        string? departmentCode,
        int? fillSlaHours,
        int? completionSlaHours,
        string? updatedBy,
        DateTime utcNow)
    {
        Apply(nameEn, nameAr, descriptionEn, descriptionAr, formDefinitionId, departmentCode, fillSlaHours, completionSlaHours);
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

    private void Apply(
        string nameEn,
        string nameAr,
        string? descriptionEn,
        string? descriptionAr,
        Guid formDefinitionId,
        string? departmentCode,
        int? fillSlaHours,
        int? completionSlaHours)
    {
        if (string.IsNullOrWhiteSpace(nameEn) || string.IsNullOrWhiteSpace(nameAr))
        {
            throw new DomainException("A task type must have an English and an Arabic name.");
        }

        if (formDefinitionId == Guid.Empty)
        {
            throw new DomainException("A task type must name the form its tasks are filled with.");
        }

        EnsureSla(fillSlaHours, "fill");
        EnsureSla(completionSlaHours, "completion");

        NameEn = nameEn.Trim();
        NameAr = nameAr.Trim();
        DescriptionEn = Normalize(descriptionEn);
        DescriptionAr = Normalize(descriptionAr);
        FormDefinitionId = formDefinitionId;
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
