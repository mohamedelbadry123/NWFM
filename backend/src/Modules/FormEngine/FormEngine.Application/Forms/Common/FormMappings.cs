using FormEngine.Application.Forms.Models;
using FormEngine.Domain.Entities;

namespace FormEngine.Application.Forms.Common;

/// <summary>Pure mapping helpers between form entities and their DTOs.</summary>
internal static class FormMappings
{
    public static FormDetailDto ToDetailDto(this FormDefinition form) => new()
    {
        Id = form.Id,
        Code = form.Code,
        NameEn = form.NameEn,
        NameAr = form.NameAr,
        Category = form.Category,
        Status = form.Status,
        DepartmentCode = form.DepartmentCode,
        CurrentVersionNo = form.CurrentVersionNo,
        IsActive = form.IsActive,
        CreatedBy = form.CreatedBy,
        UpdatedBy = form.UpdatedBy,
        CreatedAt = form.CreatedAt,
        UpdatedAt = form.UpdatedAt,
        SchemaJson = form.SchemaJson,
    };

    public static FormVersionDto ToVersionDto(this FormVersion version, FormDefinition form) => new()
    {
        Id = version.Id,
        FormDefinitionId = form.Id,
        Code = form.Code,
        NameEn = form.NameEn,
        NameAr = form.NameAr,
        FormStatus = form.Status,
        VersionNo = version.VersionNo,
        CurrentVersionNo = form.CurrentVersionNo,
        TargetClient = version.TargetClient,
        PublishedBy = version.PublishedBy,
        PublishedAt = version.PublishedAt,
        SchemaJson = version.SchemaJson,
        SnapshotJson = version.SnapshotJson,
        AcceptsSubmissions = form.AcceptsSubmissions,
    };
}
