using Auth.Domain.Constants;
using NWFM.Shared.Domain;
using NWFM.Shared.Exceptions;

namespace Auth.Domain.Entities;

/// <summary>
/// One row of an owner's coverage: a territory (or everywhere), narrowed to some departments and
/// task types. No departments means every department; no task types means every type.
/// </summary>
public sealed class OrgScope : Entity
{
    private readonly List<OrgScopeDepartment> _departments = [];
    private readonly List<OrgScopeTaskType> _taskTypes = [];

    private OrgScope() { }

    private OrgScope(string ownerType, string ownerId, string? level, string? code)
    {
        OwnerType = ownerType;
        OwnerId = ownerId;
        Level = level;
        Code = code;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public string OwnerType { get; private set; } = default!;
    public string OwnerId { get; private set; } = default!;
    public string? Level { get; private set; }
    public string? Code { get; private set; }
    public bool IsActive { get; private set; }
    public bool HasTerritory => Level is not null && Code is not null;

    /// <summary>The departments this row covers (<c>OrgScopesDepartment</c>); empty for all of them.</summary>
    public IReadOnlyCollection<OrgScopeDepartment> Departments => _departments.AsReadOnly();

    /// <summary>The task types this row covers (<c>OrgScopesTaskTypes</c>); empty for all of them.</summary>
    public IReadOnlyCollection<OrgScopeTaskType> TaskTypes => _taskTypes.AsReadOnly();

    public static OrgScope Create(
        string ownerType,
        string ownerId,
        string? level,
        string? code,
        IEnumerable<string>? departmentCodes = null,
        IEnumerable<Guid>? taskTypeIds = null)
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

        var departments = Distinct(departmentCodes);
        var taskTypes = (taskTypeIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();

        if (normalizedLevel is null && departments.Count == 0 && taskTypes.Count == 0)
            throw new DomainException("A scope must name a territory, a department, a task type, or a mix of them.");

        var scope = new OrgScope(ownerType, ownerId.Trim(), normalizedLevel, normalizedCode);
        scope._departments.AddRange(departments.Select(d => new OrgScopeDepartment(scope.Id, d)));
        scope._taskTypes.AddRange(taskTypes.Select(t => new OrgScopeTaskType(scope.Id, t)));
        return scope;
    }

    private static List<string> Distinct(IEnumerable<string>? codes) =>
        (codes ?? []).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

/// <summary>One department an org scope covers. Table: <c>OrgScopesDepartment</c>.</summary>
public sealed class OrgScopeDepartment
{
    private OrgScopeDepartment() { }

    internal OrgScopeDepartment(Guid orgScopeId, string departmentCode)
    {
        OrgScopeId = orgScopeId;
        DepartmentCode = departmentCode;
    }

    public Guid OrgScopeId { get; private set; }

    /// <summary><c>Auth.LKP_DEPARTMENT.Code</c>.</summary>
    public string DepartmentCode { get; private set; } = default!;
}

/// <summary>One task type an org scope covers. Table: <c>OrgScopesTaskTypes</c>.</summary>
public sealed class OrgScopeTaskType
{
    private OrgScopeTaskType() { }

    internal OrgScopeTaskType(Guid orgScopeId, Guid taskTypeId)
    {
        OrgScopeId = orgScopeId;
        TaskTypeId = taskTypeId;
    }

    public Guid OrgScopeId { get; private set; }

    /// <summary><c>Task.TaskTypes.Id</c> — owned by the Tasks module, so not a foreign key here.</summary>
    public Guid TaskTypeId { get; private set; }
}
