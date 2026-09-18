using NWFM.Shared.Domain;
using NWFM.Shared.Exceptions;

namespace Auth.Domain.Entities.Lookups;

public sealed class OperationArea : Entity
{
    private OperationArea() { }

    private OperationArea(string code, string nameEn, string nameAr, string cbuCode, string? mainAreaCode)
    {
        Code = code;
        NameEn = nameEn;
        NameAr = nameAr;
        CbuCode = cbuCode;
        MainAreaCode = mainAreaCode;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public string Code { get; private set; } = default!;
    public string NameEn { get; private set; } = default!;
    public string NameAr { get; private set; } = default!;
    public string CbuCode { get; private set; } = default!;
    public string? MainAreaCode { get; private set; }
    public bool IsActive { get; private set; }

    public static OperationArea Create(string code, string nameEn, string nameAr, string cbuCode, string? mainAreaCode)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("Operation area code is required.");
        return new OperationArea(code.Trim(), nameEn.Trim(), nameAr.Trim(), cbuCode.Trim(), mainAreaCode?.Trim());
    }
}
