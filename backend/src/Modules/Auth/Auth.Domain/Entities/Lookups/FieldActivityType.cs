using NWFM.Shared.Domain;
using NWFM.Shared.Exceptions;

namespace Auth.Domain.Entities.Lookups;

public sealed class FieldActivityType : Entity
{
    private FieldActivityType() { }
    public string Code { get; private set; } = "";
    public string DepartmentCode { get; private set; } = "";
    public string NameEn { get; private set; } = "";
    public string NameAr { get; private set; } = "";
    public bool IsActive { get; private set; }

    public static FieldActivityType Create(string code, string nameEn, string nameAr, string departmentCode)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(departmentCode))
            throw new DomainException("Field Activity Type requires a code and department.");
        var item = new FieldActivityType { Code = code.Trim(), CreatedAt = DateTime.UtcNow, IsActive = true };
        item.Update(nameEn, nameAr, departmentCode);
        return item;
    }
    public void Update(string nameEn, string nameAr, string departmentCode)
    {
        if (string.IsNullOrWhiteSpace(nameEn) || string.IsNullOrWhiteSpace(departmentCode))
            throw new DomainException("Name and department are required.");
        NameEn = nameEn.Trim(); NameAr = nameAr.Trim(); DepartmentCode = departmentCode.Trim();
        SetUpdated(DateTime.UtcNow);
    }
    public void SetActive(bool active) { IsActive = active; SetUpdated(DateTime.UtcNow); }
}
