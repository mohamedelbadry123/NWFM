using NWFM.Shared.Domain;
using NWFM.Shared.Exceptions;

namespace Auth.Domain.Entities.Lookups;

public sealed class Branch : Entity
{
    private Branch() { }

    private Branch(string code, string nameEn, string nameAr, string? cbuCode, string? branchCode)
    {
        Code = code;
        NameEn = nameEn;
        NameAr = nameAr;
        CbuCode = cbuCode;
        BranchCode = branchCode;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public string Code { get; private set; } = default!;
    public string NameEn { get; private set; } = default!;
    public string NameAr { get; private set; } = default!;
    public string? CbuCode { get; private set; }
    public string? BranchCode { get; private set; }
    public bool IsActive { get; private set; }

    public static Branch Create(string code, string nameEn, string nameAr, string? cbuCode, string? branchCode)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("Branch code is required.");
        return new Branch(code.Trim(), nameEn.Trim(), nameAr.Trim(), cbuCode?.Trim(), branchCode?.Trim());
    }
}
