using FormEngine.Domain.Constants;
using NWFM.Shared.Domain;
using NWFM.Shared.Exceptions;

namespace FormEngine.Domain.Entities;

/// <summary>
/// Aggregate root for a form. Stores the form-builder document as a single JSON schema
/// (<see cref="SchemaJson"/>, the working draft) and owns its immutable published
/// <see cref="FormVersion"/>s. A consumer — e.g. a workflow user task — pins one version by
/// <c>(FormDefinitionId, VersionNo)</c>, so republishing never changes a form under work in flight.
/// Table: <c>FE.FormDefinitions</c>.
/// </summary>
public sealed class FormDefinition : Entity
{
    public const int CodeMaxLength = 50;
    public const int NameMaxLength = 250;
    public const int DepartmentCodeMaxLength = 50;
    public const int ActorMaxLength = 256;

    private const string EmptySchema = "{}";

    private readonly List<FormVersion> _versions = [];

    private FormDefinition()
    {
    }

    private FormDefinition(
        string code,
        string nameEn,
        string nameAr,
        string category,
        string? departmentCode,
        string? createdBy,
        DateTime utcNow)
    {
        Code = code;
        NameEn = nameEn;
        NameAr = nameAr;
        Category = category;
        DepartmentCode = departmentCode;
        Status = FormStatuses.Draft;
        SchemaJson = EmptySchema;
        IsActive = true;
        CreatedBy = createdBy;
        UpdatedBy = createdBy;
        CreatedAt = utcNow;
        UpdatedAt = utcNow;
    }

    public string Code { get; private set; } = default!;
    public string NameEn { get; private set; } = default!;
    public string NameAr { get; private set; } = default!;
    public string Category { get; private set; } = default!;

    /// <summary>
    /// Owning department's code (<c>Auth.LKP_DEPARTMENT.Code</c>). A loose reference: FormEngine never
    /// reads Auth tables, so this is informational and a filter, not a foreign key.
    /// </summary>
    public string? DepartmentCode { get; private set; }

    public string Status { get; private set; } = default!;

    /// <summary>The latest published version, or null while the form has never been published.</summary>
    public int? CurrentVersionNo { get; private set; }

    /// <summary>
    /// The working form-builder document (<c>name_en</c>/<c>name_ar</c>/<c>elements</c>). This is the
    /// single source of truth for the form's fields; the client maps it to formly. Published copies
    /// are frozen in <see cref="Versions"/>.
    /// </summary>
    public string SchemaJson { get; private set; } = EmptySchema;

    public bool IsActive { get; private set; }
    public string? CreatedBy { get; private set; }
    public string? UpdatedBy { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<FormVersion> Versions => _versions.AsReadOnly();

    /// <summary>True once any version exists — the form can then be filled against that version.</summary>
    public bool HasPublishedVersion => CurrentVersionNo is not null;

    /// <summary>
    /// Whether new submissions are accepted. A published version must exist and the form must not be
    /// deprecated or archived. A draft revision of a published form is still fillable, against the
    /// version it was published as — reopening the designer must not block work pinned to it.
    /// </summary>
    public bool AcceptsSubmissions => HasPublishedVersion && !FormStatuses.IsFrozen(Status);

    public static FormDefinition Create(
        string code,
        string nameEn,
        string nameAr,
        string category,
        string? departmentCode,
        string? createdBy,
        DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainException("A form must have a code.");
        }

        EnsureNames(nameEn, nameAr);
        EnsureCategory(category);

        return new FormDefinition(
            code.Trim(),
            nameEn.Trim(),
            nameAr.Trim(),
            category,
            NormalizeOptional(departmentCode),
            NormalizeOptional(createdBy),
            utcNow);
    }

    /// <summary>
    /// Updates names, category and department. Deliberately not routed through
    /// <see cref="EnsureEditable"/>: main info is not a schema change, so a Published form keeps its
    /// status. Deprecated and archived forms stay blocked.
    /// </summary>
    public void UpdateDetails(
        string nameEn,
        string nameAr,
        string category,
        string? departmentCode,
        string? updatedBy,
        DateTime utcNow)
    {
        EnsureNotFrozen();
        EnsureNames(nameEn, nameAr);
        EnsureCategory(category);

        NameEn = nameEn.Trim();
        NameAr = nameAr.Trim();
        Category = category;
        DepartmentCode = NormalizeOptional(departmentCode);
        Touch(updatedBy, utcNow);
    }

    /// <summary>
    /// Replaces the form-builder document. Editing a Published form reopens it as a Draft revision;
    /// its published versions stay untouched. The names are synced from the schema when provided.
    /// </summary>
    public void SetSchema(string schemaJson, string? nameEn, string? nameAr, string? updatedBy, DateTime utcNow)
    {
        EnsureEditable();

        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            throw new DomainException("A form schema cannot be empty.");
        }

        SchemaJson = schemaJson;

        if (!string.IsNullOrWhiteSpace(nameEn))
        {
            NameEn = ClipName(nameEn);
        }

        if (!string.IsNullOrWhiteSpace(nameAr))
        {
            NameAr = ClipName(nameAr);
        }

        Touch(updatedBy, utcNow);
    }

    /// <summary>
    /// Freezes the current draft schema into a new version and marks the form Published. Existing
    /// version rows are never mutated. The caller guarantees the schema has fields.
    /// </summary>
    public FormVersion Publish(string? publishedBy, IReadOnlyCollection<FormVersionSnapshot> snapshots, DateTime utcNow)
    {
        if (FormStatuses.IsFrozen(Status))
        {
            throw new DomainException($"A {Status} form cannot be published.");
        }

        if (string.IsNullOrWhiteSpace(SchemaJson) || SchemaJson == EmptySchema)
        {
            throw new DomainException("A form must have a schema before publishing.");
        }

        if (snapshots is null || snapshots.Count == 0)
        {
            throw new DomainException("At least one version snapshot is required to publish.");
        }

        var newVersionNo = (CurrentVersionNo ?? 0) + 1;
        FormVersion? primary = null;

        foreach (var snapshot in snapshots)
        {
            var version = FormVersion.Create(
                Id,
                newVersionNo,
                snapshot.TargetClient,
                snapshot.SchemaJson,
                snapshot.SnapshotJson,
                NormalizeOptional(publishedBy),
                utcNow);

            _versions.Add(version);
            primary ??= version;
        }

        CurrentVersionNo = newVersionNo;
        Status = FormStatuses.Published;
        Touch(publishedBy, utcNow);
        return primary!;
    }

    public void Deprecate(string? updatedBy, DateTime utcNow)
    {
        if (Status != FormStatuses.Published)
        {
            throw new DomainException($"Only a Published form can be deprecated (current: {Status}).");
        }

        Status = FormStatuses.Deprecated;
        Touch(updatedBy, utcNow);
    }

    public void Archive(string? updatedBy, DateTime utcNow)
    {
        if (Status == FormStatuses.Archived)
        {
            throw new DomainException("Form is already archived.");
        }

        Status = FormStatuses.Archived;
        IsActive = false;
        Touch(updatedBy, utcNow);
    }

    /// <summary>
    /// Produces a copy of the working schema as a fresh Draft form under a new code. Version history
    /// is not carried over.
    /// </summary>
    public FormDefinition Clone(string newCode, string newNameEn, string newNameAr, string? createdBy, DateTime utcNow)
    {
        var clone = Create(newCode, newNameEn, newNameAr, Category, DepartmentCode, createdBy, utcNow);
        clone.SchemaJson = SchemaJson;
        return clone;
    }

    /// <summary>Blocks edits on Deprecated/Archived forms without changing Status.</summary>
    private void EnsureNotFrozen()
    {
        if (FormStatuses.IsFrozen(Status))
        {
            throw new DomainException($"A {Status} form cannot be edited.");
        }
    }

    /// <summary>
    /// Guards schema edits. Editing a Published form opens a new draft revision; Deprecated/Archived
    /// forms are immutable.
    /// </summary>
    private void EnsureEditable()
    {
        EnsureNotFrozen();

        if (Status == FormStatuses.Published)
        {
            Status = FormStatuses.Draft;
        }
    }

    private void Touch(string? actor, DateTime utcNow)
    {
        UpdatedBy = NormalizeOptional(actor) ?? UpdatedBy;
        SetUpdated(utcNow);
    }

    private static void EnsureNames(string nameEn, string nameAr)
    {
        if (string.IsNullOrWhiteSpace(nameEn))
        {
            throw new DomainException("A form must have an English name.");
        }

        if (string.IsNullOrWhiteSpace(nameAr))
        {
            throw new DomainException("A form must have an Arabic name.");
        }
    }

    private static void EnsureCategory(string category)
    {
        if (!FormCategories.IsDefined(category))
        {
            throw new DomainException($"Unknown form category '{category}'.");
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>A name synced from the schema is free text typed into the builder; it must still fit the column.</summary>
    private static string ClipName(string name)
    {
        var trimmed = name.Trim();
        return trimmed.Length <= NameMaxLength ? trimmed : trimmed[..NameMaxLength];
    }
}
