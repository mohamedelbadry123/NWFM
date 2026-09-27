using NWFM.Shared.Domain;
using NWFM.Shared.Exceptions;

namespace Auth.Domain.Entities;

public sealed class Permission : Entity
{
    private Permission() { }

    private Permission(string code, string module, string nameEn, string nameAr, string? description)
    {
        Code = code;
        Module = module;
        NameEn = nameEn;
        NameAr = nameAr;
        Description = description ?? string.Empty;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public string Code { get; private set; } = default!;
    public string Module { get; private set; } = default!;
    public string NameEn { get; private set; } = default!;
    public string NameAr { get; private set; } = default!;
    public string Description { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }

    public ICollection<RolePermission> RolePermissions { get; private set; } = new List<RolePermission>();

    public static Permission Create(string code, string module, string nameEn, string nameAr, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("Permission code is required.");

        return new Permission(code.Trim(), module.Trim(), nameEn.Trim(), nameAr.Trim(), description);
    }
}
