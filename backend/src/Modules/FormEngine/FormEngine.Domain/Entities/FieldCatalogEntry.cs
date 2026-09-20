using NWFM.Shared.Domain;
using NWFM.Shared.Exceptions;

namespace FormEngine.Domain.Entities;

/// <summary>
/// A globally shared, canonical field definition. Lets the form builder reuse consistent
/// <c>data_name</c>s across forms instead of creating near-duplicate columns. One canonical
/// <see cref="FieldType"/> per <see cref="DataName"/> — and that type fixes the SQL type of the
/// shared <c>FE.Submissions</c> column for every form that uses the name. Table: <c>FE.FieldCatalog</c>.
/// </summary>
public sealed class FieldCatalogEntry : Entity
{
    public const int DataNameMaxLength = 200;
    public const int FieldTypeMaxLength = 50;
    public const int LabelMaxLength = 500;
    public const int DescriptionMaxLength = 1000;

    private FieldCatalogEntry()
    {
    }

    private FieldCatalogEntry(string dataName, string fieldType, string? labelEn, string? labelAr, string? description)
    {
        DataName = dataName;
        FieldType = fieldType;
        LabelEn = labelEn;
        LabelAr = labelAr;
        Description = description;
        IsActive = true;
    }

    /// <summary>Canonical, unique key (matches the builder field's <c>data_name</c>).</summary>
    public string DataName { get; private set; } = default!;

    /// <summary>Canonical builder element type (e.g. <c>text</c>, <c>numeric</c>, <c>single_choice</c>).</summary>
    public string FieldType { get; private set; } = default!;

    public string? LabelEn { get; private set; }
    public string? LabelAr { get; private set; }
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }

    public static FieldCatalogEntry Create(string dataName, string fieldType, string? labelEn = null, string? labelAr = null, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(dataName))
        {
            throw new DomainException("A catalog entry must have a data name.");
        }

        if (string.IsNullOrWhiteSpace(fieldType))
        {
            throw new DomainException("A catalog entry must have a field type.");
        }

        return new FieldCatalogEntry(
            dataName.Trim(),
            fieldType.Trim(),
            Clip(labelEn, LabelMaxLength),
            Clip(labelAr, LabelMaxLength),
            Clip(description, DescriptionMaxLength));
    }

    public void UpdateLabels(string? labelEn, string? labelAr, string? description, DateTime utcNow)
    {
        LabelEn = Clip(labelEn, LabelMaxLength);
        LabelAr = Clip(labelAr, LabelMaxLength);
        Description = Clip(description, DescriptionMaxLength);
        SetUpdated(utcNow);
    }

    private static string? Clip(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
