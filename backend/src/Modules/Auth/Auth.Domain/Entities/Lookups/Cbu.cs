using NWFM.Shared.Domain;
using NWFM.Shared.Exceptions;

namespace Auth.Domain.Entities.Lookups;

public sealed class Cbu : Entity
{
    private Cbu() { }

    private Cbu(string code, string nameEn, string nameAr, string clusterCode)
    {
        Code = code;
        NameEn = nameEn;
        NameAr = nameAr;
        ClusterCode = clusterCode;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public string Code { get; private set; } = default!;
    public string NameEn { get; private set; } = default!;
    public string NameAr { get; private set; } = default!;
    public string ClusterCode { get; private set; } = default!;
    public bool IsActive { get; private set; }

    public static Cbu Create(string code, string nameEn, string nameAr, string clusterCode)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("CBU code is required.");
        return new Cbu(code.Trim(), nameEn.Trim(), nameAr.Trim(), clusterCode.Trim());
    }
}
