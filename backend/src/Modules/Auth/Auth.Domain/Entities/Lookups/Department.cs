using NWFM.Shared.Domain;
using NWFM.Shared.Exceptions;

namespace Auth.Domain.Entities.Lookups;

public sealed class Department : Entity
{
    private Department() { }

    private Department(string code, string nameEn, string nameAr)
    {
        Code = code;
        NameEn = nameEn;
        NameAr = nameAr;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public string Code { get; private set; } = default!;
    public string NameEn { get; private set; } = default!;
    public string NameAr { get; private set; } = default!;
    public bool IsActive { get; private set; }

    public static Department Create(string code, string nameEn, string nameAr)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("Department code is required.");
        return new Department(code.Trim(), nameEn.Trim(), nameAr.Trim());
    }
}
