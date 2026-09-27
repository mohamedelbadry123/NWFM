using NWFM.Shared.Integration.Forms;
using Tasks.Application.TaskTypes.Models;
using Tasks.Domain.Entities;

namespace Tasks.Application.TaskTypes.Common;

/// <summary>Shapes task types for the API, with the forms each is bound to, resolved once per form.</summary>
internal static class TaskTypeProjection
{
    public static async Task<IReadOnlyList<TaskTypeDto>> ToDtosAsync(
        IFormGateway forms,
        IReadOnlyList<TaskType> types,
        CancellationToken ct)
    {
        var formInfo = new Dictionary<Guid, PublishedFormInfo?>();

        foreach (var formId in types.SelectMany(t => t.Forms).Select(f => f.FormDefinitionId).Distinct())
        {
            formInfo[formId] = await forms.FindPublishedAsync(formId, ct);
        }

        return types
            .Select(type => new TaskTypeDto
            {
                Id = type.Id,
                Code = type.Code,
                NameEn = type.NameEn,
                NameAr = type.NameAr,
                DescriptionEn = type.DescriptionEn,
                DescriptionAr = type.DescriptionAr,
                Forms = type.Forms
                    .OrderBy(f => f.SortOrder)
                    .Select(f =>
                    {
                        var form = formInfo.GetValueOrDefault(f.FormDefinitionId);

                        return new TaskTypeFormDto(
                            f.FormDefinitionId,
                            form?.Code,
                            form?.NameEn,
                            form?.NameAr,
                            form is { AcceptsSubmissions: true } ? form.CurrentVersionNo : null,
                            f.SortOrder,
                            f.IsC2mClosingForm);
                    })
                    .ToList(),
                DepartmentCode = type.DepartmentCode,
                FillSlaHours = type.FillSlaHours,
                CompletionSlaHours = type.CompletionSlaHours,
                ClosesC2mActivity = type.ClosesC2mActivity,
                IsActive = type.IsActive,
                CreatedAt = type.CreatedAt,
                UpdatedAt = type.UpdatedAt,
            })
            .ToList();
    }
}
