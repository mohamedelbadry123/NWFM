using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Constants;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Results;
using NWFM.Shared.Security;
using Tasks.Application.Common.Interfaces;
using Tasks.Application.Tasks.Models;
using Tasks.Domain.Constants;

namespace Tasks.Application.Tasks.Queries.GetTaskComputedColumns;

/// <summary>
/// The computed columns the task grid can show. For one task type: its forms' columns, in the type's
/// order, then those of forms added to its tasks. With no type: every active type's forms and every
/// form ever added to a task, so a reader can pick a column across types. Each column is read from
/// its form's current published version.
/// </summary>
[Authorize(Policy = NwfmPolicies.ViewTasks)]
public sealed record GetTaskComputedColumnsQuery(Guid? TaskTypeId) : IRequest<Result<IReadOnlyList<TaskComputedColumnDto>>>;

public sealed class GetTaskComputedColumnsQueryHandler(ITasksDbContext db, IFormGateway forms)
    : IRequestHandler<GetTaskComputedColumnsQuery, Result<IReadOnlyList<TaskComputedColumnDto>>>
{
    public async Task<Result<IReadOnlyList<TaskComputedColumnDto>>> Handle(GetTaskComputedColumnsQuery request, CancellationToken ct)
    {
        List<Guid> typeFormIds;
        List<Guid> extraFormIds;

        if (request.TaskTypeId is Guid typeId)
        {
            typeFormIds = await db.TaskTypeForms
                .AsNoTracking()
                .Where(f => f.TaskTypeId == typeId)
                .OrderBy(f => f.SortOrder)
                .Select(f => f.FormDefinitionId)
                .ToListAsync(ct);

            extraFormIds = await db.TaskForms
                .AsNoTracking()
                .Where(f => f.Source == TaskFormSources.Extra && db.Tasks.Any(t => t.Id == f.FieldTaskId && t.TaskTypeId == typeId))
                .Select(f => f.FormDefinitionId)
                .Distinct()
                .ToListAsync(ct);
        }
        else
        {
            typeFormIds = await db.TaskTypeForms
                .AsNoTracking()
                .Where(f => db.TaskTypes.Any(t => t.Id == f.TaskTypeId && t.IsActive))
                .OrderBy(f => f.TaskTypeId)
                .ThenBy(f => f.SortOrder)
                .Select(f => f.FormDefinitionId)
                .ToListAsync(ct);

            extraFormIds = await db.TaskForms
                .AsNoTracking()
                .Where(f => f.Source == TaskFormSources.Extra)
                .Select(f => f.FormDefinitionId)
                .Distinct()
                .ToListAsync(ct);
        }

        var columns = new List<TaskComputedColumnDto>();

        foreach (var formId in typeFormIds.Concat(extraFormIds).Distinct())
        {
            var form = await forms.FindPublishedAsync(formId, ct);
            if (form is null)
            {
                continue;
            }

            foreach (var column in await forms.GetComputedColumnsAsync(formId, form.CurrentVersionNo, ct))
            {
                columns.Add(new TaskComputedColumnDto(
                    TaskComputedColumnIds.Of(formId, column.Key),
                    formId,
                    form.Code,
                    form.NameEn,
                    form.NameAr,
                    column.Key,
                    column.LabelEn,
                    column.LabelAr,
                    column.OutputType,
                    column.ShowInTaskGrid));
            }
        }

        return Result.Success<IReadOnlyList<TaskComputedColumnDto>>(columns);
    }
}
