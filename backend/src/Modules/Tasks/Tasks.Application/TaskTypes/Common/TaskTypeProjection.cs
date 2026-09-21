using NWFM.Shared.Integration.Forms;
using Tasks.Application.TaskTypes.Models;
using Tasks.Domain.Entities;

namespace Tasks.Application.TaskTypes.Common;

/// <summary>Shapes task types for the API, with the form each is bound to, resolved once per form.</summary>
internal static class TaskTypeProjection
{
    public static async Task<IReadOnlyList<TaskTypeDto>> ToDtosAsync(
        IFormGateway forms,
        IReadOnlyList<TaskType> types,
        CancellationToken ct)
    {
        var formInfo = new Dictionary<Guid, PublishedFormInfo?>();

        foreach (var formId in types.Select(t => t.FormDefinitionId).Distinct())
        {
            formInfo[formId] = await forms.FindPublishedAsync(formId, ct);
        }

        return types
            .Select(type =>
            {
                var form = formInfo.GetValueOrDefault(type.FormDefinitionId);

                return new TaskTypeDto
                {
                    Id = type.Id,
                    Code = type.Code,
                    NameEn = type.NameEn,
                    NameAr = type.NameAr,
                    DescriptionEn = type.DescriptionEn,
                    DescriptionAr = type.DescriptionAr,
                    FormDefinitionId = type.FormDefinitionId,
                    FormCode = form?.Code,
                    FormNameEn = form?.NameEn,
                    FormNameAr = form?.NameAr,
                    FormCurrentVersionNo = form is { AcceptsSubmissions: true } ? form.CurrentVersionNo : null,
                    DepartmentCode = type.DepartmentCode,
                    FillSlaHours = type.FillSlaHours,
                    CompletionSlaHours = type.CompletionSlaHours,
                    IsActive = type.IsActive,
                    CreatedAt = type.CreatedAt,
                    UpdatedAt = type.UpdatedAt,
                };
            })
            .ToList();
    }
}
