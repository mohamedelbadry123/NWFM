namespace Tenancy.Domain.Entities;

/// <summary>A sellable plan and the limits it carries. A null limit means unlimited.</summary>
public sealed class Plan
{
    public const int CodeMaxLength = 50;
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 1000;

    private Plan()
    {
    }

    public int Id { get; private set; }

    public string Code { get; private set; } = default!;

    public string NameEn { get; private set; } = default!;

    public string NameAr { get; private set; } = default!;

    public string? DescriptionEn { get; private set; }

    public string? DescriptionAr { get; private set; }

    public int? MaxUsers { get; private set; }

    public int? MaxTeams { get; private set; }

    public int? MaxStorageGb { get; private set; }

    /// <summary>Length of the trial a signup on this plan starts with; 0 starts it active.</summary>
    public int TrialDays { get; private set; }

    /// <summary>Offered on the public signup page.</summary>
    public bool IsPublic { get; private set; }

    public bool IsActive { get; private set; }

    public int SortOrder { get; private set; }

    public static Plan Create(string code, string nameEn, string nameAr, string? descriptionEn, string? descriptionAr,
        int? maxUsers, int? maxTeams, int? maxStorageGb, int trialDays, bool isPublic, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainException("A plan must have a code.");
        }

        var plan = new Plan { Code = code.Trim().ToLowerInvariant(), IsActive = true };
        plan.Update(nameEn, nameAr, descriptionEn, descriptionAr, maxUsers, maxTeams, maxStorageGb, trialDays, isPublic, sortOrder);
        return plan;
    }

    public void Update(string nameEn, string nameAr, string? descriptionEn, string? descriptionAr,
        int? maxUsers, int? maxTeams, int? maxStorageGb, int trialDays, bool isPublic, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(nameEn) || string.IsNullOrWhiteSpace(nameAr))
        {
            throw new DomainException("A plan must have an English and an Arabic name.");
        }

        if (maxUsers < 1 || maxTeams < 0 || maxStorageGb < 1 || trialDays < 0)
        {
            throw new DomainException("Plan limits must be positive.");
        }

        NameEn = nameEn.Trim();
        NameAr = nameAr.Trim();
        DescriptionEn = descriptionEn?.Trim();
        DescriptionAr = descriptionAr?.Trim();
        MaxUsers = maxUsers;
        MaxTeams = maxTeams;
        MaxStorageGb = maxStorageGb;
        TrialDays = trialDays;
        IsPublic = isPublic;
        SortOrder = sortOrder;
    }

    public void SetActive(bool isActive) => IsActive = isActive;
}
