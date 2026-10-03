using NWFM.Shared.Domain;
using NWFM.Shared.Exceptions;

namespace Auth.Domain.Entities.Lookups;

/// <summary>
/// A kind of activity (shown as "Activity Types"). Not tied to a department: forms, workflows and SLA
/// policies pair it with one of their own. Names the sources allowed to create it — at least one.
/// </summary>
public sealed class FieldActivityType : Entity
{
    private readonly List<FieldActivityTypeSource> _sources = [];

    private FieldActivityType() { }
    public string Code { get; private set; } = "";
    public string NameEn { get; private set; } = "";
    public string NameAr { get; private set; } = "";
    public bool IsActive { get; private set; }

    /// <summary>The sources allowed to create an activity of this type.</summary>
    public IReadOnlyCollection<FieldActivityTypeSource> Sources => _sources.AsReadOnly();

    public static FieldActivityType Create(string code, string nameEn, string nameAr, IEnumerable<string> sourceCodes)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("Activity Type requires a code.");
        var item = new FieldActivityType { Code = code.Trim(), CreatedAt = DateTime.UtcNow, IsActive = true };
        item.Update(nameEn, nameAr, sourceCodes);
        return item;
    }

    public void Update(string nameEn, string nameAr, IEnumerable<string> sourceCodes)
    {
        if (string.IsNullOrWhiteSpace(nameEn))
            throw new DomainException("Name is required.");
        var codes = (sourceCodes ?? []).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (codes.Count == 0)
            throw new DomainException("Choose at least one source allowed to create this activity type.");
        NameEn = nameEn.Trim(); NameAr = nameAr.Trim();
        _sources.RemoveAll(s => !codes.Contains(s.SourceCode, StringComparer.OrdinalIgnoreCase));
        foreach (var code in codes.Where(c => !_sources.Any(s => string.Equals(s.SourceCode, c, StringComparison.OrdinalIgnoreCase))))
            _sources.Add(new FieldActivityTypeSource(Id, code));
        SetUpdated(DateTime.UtcNow);
    }

    public void SetActive(bool active) { IsActive = active; SetUpdated(DateTime.UtcNow); }
}

/// <summary>One source allowed to create activities of a type.</summary>
public sealed class FieldActivityTypeSource
{
    private FieldActivityTypeSource() { }
    internal FieldActivityTypeSource(Guid fieldActivityTypeId, string sourceCode)
    {
        FieldActivityTypeId = fieldActivityTypeId;
        SourceCode = sourceCode;
    }

    public Guid FieldActivityTypeId { get; private set; }
    public string SourceCode { get; private set; } = "";
}
