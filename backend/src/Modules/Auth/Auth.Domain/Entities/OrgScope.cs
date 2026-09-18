using Auth.Domain.Constants;
using NWFM.Shared.Domain;
using NWFM.Shared.Exceptions;

namespace Auth.Domain.Entities;

public sealed class OrgScope : Entity
{
    private OrgScope() { }

    private OrgScope(string ownerType, string ownerId, string? level, string? code, string? departmentId)
    {
        OwnerType = ownerType;
        OwnerId = ownerId;
        Level = level;
        Code = code;
        DepartmentId = departmentId;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public string OwnerType { get; private set; } = default!;
    public string OwnerId { get; private set; } = default!;
    public string? Level { get; private set; }
    public string? Code { get; private set; }
    public string? DepartmentId { get; private set; }
    public bool IsActive { get; private set; }
    public bool HasTerritory => Level is not null && Code is not null;

    public static OrgScope Create(string ownerType, string ownerId, string? level, string? code, string? departmentId)
    {
        if (!OrgScopeOwnerTypes.IsDefined(ownerType))
            throw new DomainException($"Unknown scope owner type '{ownerType}'.");

        if (string.IsNullOrWhiteSpace(ownerId))
            throw new DomainException("A scope must belong to an owner.");

        var normalizedLevel = string.IsNullOrWhiteSpace(level) ? null : level.Trim();
        var normalizedCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim();

        if (normalizedLevel is null != normalizedCode is null)
            throw new DomainException("A scope must name both a level and a code, or neither.");

        if (normalizedLevel is not null && !OrgScopeLevels.IsDefined(normalizedLevel))
            throw new DomainException($"Unknown scope level '{normalizedLevel}'.");

        if (normalizedLevel is null && departmentId is null)
            throw new DomainException("A scope must name a territory, a department, or both.");

        return new OrgScope(ownerType, ownerId.Trim(), normalizedLevel, normalizedCode, departmentId);
    }
}
