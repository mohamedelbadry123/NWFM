using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Integration.Organization;
using Tasks.Application.Common.Interfaces;
using Tasks.Application.Tasks.Models;
using Tasks.Domain.Entities;

namespace Tasks.Application.Tasks.Common;

/// <summary>
/// Shapes tasks for the API. Names that live in other modules — the team, the form's current version
/// — are resolved once per page rather than once per row.
/// </summary>
internal static class TaskProjection
{
    public static async Task<IReadOnlyList<TaskListItemDto>> ToListItemsAsync(
        ITasksDbContext db,
        IOrgDirectory directory,
        IFormGateway forms,
        IReadOnlyList<FieldTask> tasks,
        CancellationToken ct)
    {
        if (tasks.Count == 0)
        {
            return [];
        }

        var typeIds = tasks.Select(t => t.TaskTypeId).Distinct().ToList();
        var types = await db.TaskTypes
            .AsNoTracking()
            .Where(t => typeIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, ct);

        var teamIds = tasks
            .Select(t => t.ActiveAssignment?.TeamId)
            .OfType<Guid>()
            .Distinct()
            .ToList();
        var teams = await directory.GetTeamsAsync(teamIds, ct);

        var currentVersions = await CurrentVersionsAsync(forms, tasks.Select(t => t.FormDefinitionId), ct);

        return tasks
            .Select(task =>
            {
                types.TryGetValue(task.TaskTypeId, out var type);
                var teamId = task.ActiveAssignment?.TeamId;
                var teamName = teamId is Guid id && teams.TryGetValue(id, out var team) ? team.Name : null;
                currentVersions.TryGetValue(task.FormDefinitionId, out var current);

                return ToListItem(task, type, teamName, current);
            })
            .ToList();
    }

    public static TaskListItemDto ToListItem(FieldTask task, TaskType? type, string? teamName, int? formCurrentVersionNo) =>
        new()
        {
            Id = task.Id,
            TaskNumber = task.TaskNumber,
            TaskTypeId = task.TaskTypeId,
            TaskTypeCode = type?.Code,
            TaskTypeNameEn = type?.NameEn,
            TaskTypeNameAr = type?.NameAr,
            FormDefinitionId = task.FormDefinitionId,
            FormVersionNo = task.FormVersionNo,
            FormCurrentVersionNo = formCurrentVersionNo,
            Status = task.Status,
            Priority = task.Priority,
            Source = task.Source,
            Title = task.Title,
            ExternalReference = task.ExternalReference,
            Latitude = task.Latitude,
            Longitude = task.Longitude,
            Address = task.Address,
            CbuCode = task.CbuCode,
            BranchCode = task.BranchCode,
            OperationAreaCode = task.OperationAreaCode,
            DepartmentCode = task.DepartmentCode,
            AssignedTeamId = task.ActiveAssignment?.TeamId,
            AssignedTeamName = teamName,
            DueDate = task.DueDate,
            CompletionDueDate = task.CompletionDueDate,
            AssignedDate = task.AssignedDate,
            SubmittedDate = task.SubmittedDate,
            SubmissionCount = task.SubmissionCount,
            ReturnReasonCode = task.ReturnReasonCode,
            ReturnReason = task.ReturnReason,
            ReturnedDate = task.ReturnedDate,
            ReturnCount = task.ReturnCount,
            FaId = task.FaId,
            WfmTicketId = task.WfmTicketId,
            C2mStatus = task.C2mStatus,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt,
        };

    /// <summary>Each form's current published version. A page holds tasks of a handful of types, so a handful of reads.</summary>
    public static async Task<Dictionary<Guid, int?>> CurrentVersionsAsync(
        IFormGateway forms,
        IEnumerable<Guid> formIds,
        CancellationToken ct)
    {
        var versions = new Dictionary<Guid, int?>();

        foreach (var formId in formIds.Distinct())
        {
            var form = await forms.FindPublishedAsync(formId, ct);
            versions[formId] = form is { AcceptsSubmissions: true } ? form.CurrentVersionNo : null;
        }

        return versions;
    }
}
