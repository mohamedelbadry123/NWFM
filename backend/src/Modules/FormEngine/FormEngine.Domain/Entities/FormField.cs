using NWFM.Shared.Domain;
using NWFM.Shared.Exceptions;

namespace FormEngine.Domain.Entities;

/// <summary>
/// One column of a form's submission table: the <c>data_name</c> a published version introduced, and
/// the builder type its column was created as. A name keeps that type for the life of its form —
/// the column already holds values of it — but another form is free to use the same name as another
/// type, since it writes to a table of its own. Table: <c>FE.FormFields</c>.
/// </summary>
public sealed class FormField : Entity
{
    public const int DataNameMaxLength = 200;
    public const int FieldTypeMaxLength = 50;
    public const int LabelMaxLength = 500;

    private FormField()
    {
    }

    private FormField(
        Guid formDefinitionId,
        string dataName,
        string fieldType,
        string? labelEn,
        string? labelAr,
        bool isCompanion,
        int versionNo,
        DateTime utcNow)
    {
        FormDefinitionId = formDefinitionId;
        DataName = dataName;
        FieldType = fieldType;
        LabelEn = labelEn;
        LabelAr = labelAr;
        IsCompanion = isCompanion;
        FirstVersionNo = versionNo;
        LastVersionNo = versionNo;
        CreatedAt = utcNow;
        UpdatedAt = utcNow;
    }

    public Guid FormDefinitionId { get; private set; }

    /// <summary>The field's <c>data_name</c>, which is also its column name.</summary>
    public string DataName { get; private set; } = default!;

    /// <summary>The builder element type the column was created for (e.g. <c>numeric</c>, <c>single_choice</c>).</summary>
    public string FieldType { get; private set; } = default!;

    /// <summary>Labels as the most recent version to carry the field had them.</summary>
    public string? LabelEn { get; private set; }

    public string? LabelAr { get; private set; }

    /// <summary>
    /// True for a <c>&lt;data_name&gt;_other</c> column: the free text a choice field's "Other" option
    /// carries. It is a real column, but no field in the builder is called that.
    /// </summary>
    public bool IsCompanion { get; private set; }

    /// <summary>The version that introduced the column.</summary>
    public int FirstVersionNo { get; private set; }

    /// <summary>The latest version that still declares it. A field a later version dropped keeps its column.</summary>
    public int LastVersionNo { get; private set; }

    public static FormField Create(
        Guid formDefinitionId,
        string dataName,
        string fieldType,
        string? labelEn,
        string? labelAr,
        bool isCompanion,
        int versionNo,
        DateTime utcNow)
    {
        if (formDefinitionId == Guid.Empty)
        {
            throw new DomainException("A form field must belong to a form.");
        }

        if (string.IsNullOrWhiteSpace(dataName))
        {
            throw new DomainException("A form field must have a data name.");
        }

        if (string.IsNullOrWhiteSpace(fieldType))
        {
            throw new DomainException("A form field must have a type.");
        }

        return new FormField(
            formDefinitionId,
            dataName.Trim(),
            fieldType.Trim(),
            Clip(labelEn),
            Clip(labelAr),
            isCompanion,
            versionNo,
            utcNow);
    }

    /// <summary>Records that <paramref name="versionNo"/> still declares the field, under these labels.</summary>
    public void SeenIn(int versionNo, string? labelEn, string? labelAr, DateTime utcNow)
    {
        if (versionNo > LastVersionNo)
        {
            LastVersionNo = versionNo;
        }

        LabelEn = Clip(labelEn) ?? LabelEn;
        LabelAr = Clip(labelAr) ?? LabelAr;
        SetUpdated(utcNow);
    }

    private static string? Clip(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= LabelMaxLength ? trimmed : trimmed[..LabelMaxLength];
    }
}
