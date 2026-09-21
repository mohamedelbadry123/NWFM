using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Constants;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Integration.Organization;
using NWFM.Shared.Organization;
using NWFM.Shared.Results;
using NWFM.Shared.Security;
using Tasks.Application.Common;
using Tasks.Application.Common.Interfaces;
using Tasks.Application.Constants;
using Tasks.Domain.Constants;
using Tasks.Domain.Entities;

namespace Tasks.Application.Tasks.Queries.ExportTaskPdf;

/// <summary>
/// The task as a printable report: its facts, the latest fill's answers, and the photos and
/// signatures that fill carried. <see cref="Language"/> is <c>en</c> or <c>ar</c>; anything else
/// prints in English.
/// </summary>
[Authorize(Policy = NwfmPolicies.ViewTasks)]
public sealed record ExportTaskPdfQuery(Guid TaskId, string? Language) : IRequest<Result<TaskPdfDto>>;

public sealed record TaskPdfDto(byte[] Content, string FileName)
{
    public const string ContentType = "application/pdf";
}

public sealed class ExportTaskPdfQueryHandler(
    ITasksDbContext db,
    TaskAccess access,
    IOrgDirectory directory,
    IOrgScopeProvider scopes,
    IFormGateway forms,
    ITaskReportRenderer renderer,
    TimeProvider clock)
    : IRequestHandler<ExportTaskPdfQuery, Result<TaskPdfDto>>
{
    private const string Arabic = "ar";
    private const string English = "en";
    private const string Missing = "-";

    /// <summary>The form builder's signature type — a signature prints as a signature, not as a photo.</summary>
    private const string SignatureFieldType = "signature";

    /// <summary>Bounds on what one report embeds, so a fill with many large photos still renders.</summary>
    private const long MaxImageBytes = 10 * 1024 * 1024;

    private const int MaxEmbeddedImages = 40;

    public async Task<Result<TaskPdfDto>> Handle(ExportTaskPdfQuery request, CancellationToken ct)
    {
        var task = await access.FindAsync(request.TaskId, ct);
        if (task is null)
        {
            return Result.Failure<TaskPdfDto>(TaskErrors.Task.NotFound);
        }

        var language = string.Equals(request.Language, Arabic, StringComparison.OrdinalIgnoreCase) ? Arabic : English;
        var isArabic = language == Arabic;
        string Pick(string? en, string? ar) =>
            (isArabic ? ar : en) is { Length: > 0 } preferred ? preferred : (en ?? ar ?? string.Empty);

        var contextId = task.Id.ToString("D");

        var type = await db.TaskTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == task.TaskTypeId, ct);
        var form = await forms.FindPublishedAsync(task.FormDefinitionId, ct);

        var teamName = await ActiveTeamNameAsync(task, ct);
        var org = await OrgLabelsAsync(task, Pick, ct);

        var latest = await forms.GetLatestByContextAsync(task.FormDefinitionId, TasksSchema.FormContextType, contextId, ct);
        var display = latest is null
            ? []
            : await forms.DescribeAnswersAsync(task.FormDefinitionId, latest.VersionNo, latest.Answers, ct);

        var answers = display
            .Select(a => new TaskReportAnswer(
                Pick(a.LabelEn, a.LabelAr) is { Length: > 0 } label ? label : a.DataName,
                Pick(a.DisplayEn, a.DisplayAr)))
            .ToList();

        var files = await FilesAsync(contextId, latest, display, Pick, ct);

        var report = new TaskReport
        {
            Language = language,
            GeneratedAt = clock.GetUtcNow(),
            TaskNumber = task.TaskNumber,
            Status = task.Status,
            Priority = task.Priority,
            Source = task.Source,
            Title = task.Title,
            ExternalReference = task.ExternalReference,
            Notes = task.Notes,
            TaskType = type is null ? Missing : $"{type.Code} — {Pick(type.NameEn, type.NameAr)}",
            Form = form is null ? Missing : $"{form.Code} — {Pick(form.NameEn, form.NameAr)}",
            FormVersionNo = task.FormVersionNo,
            Cluster = org.Cluster,
            Cbu = org.Cbu,
            Branch = org.Branch,
            OperationArea = org.OperationArea,
            Department = org.Department,
            Latitude = task.Latitude,
            Longitude = task.Longitude,
            Address = task.Address,
            CreatedAt = task.CreatedAt,
            DueDate = task.DueDate,
            CompletionDueDate = task.CompletionDueDate,
            Team = teamName,
            AssignedDate = task.AssignedDate,
            SubmittedDate = task.SubmittedDate,
            SubmissionCount = task.SubmissionCount,
            CompletedDate = task.CompletedDate,
            ReturnReasonCode = task.ReturnReasonCode,
            ReturnReason = task.ReturnReason,
            ReturnedDate = task.ReturnedDate,
            ReturnCount = task.ReturnCount,
            LatestFill = latest is null
                ? null
                : new TaskReportFill(latest.SubmittedByName ?? latest.SubmittedBy, latest.SubmittedDate, latest.VersionNo),
            Answers = answers,
            Files = files,
        };

        var content = renderer.Render(report);
        var fileName = $"Task_{task.TaskNumber}_{report.GeneratedAt:yyyyMMdd}.pdf";

        return Result.Success(new TaskPdfDto(content, fileName));
    }

    private async Task<string?> ActiveTeamNameAsync(FieldTask task, CancellationToken ct)
    {
        if (task.ActiveAssignment is not { } active)
        {
            return null;
        }

        var teams = await directory.GetTeamsAsync([active.TeamId], ct);
        return teams.TryGetValue(active.TeamId, out var team) ? team.Name : null;
    }

    private sealed record OrgLabels(string Cluster, string Cbu, string Branch, string OperationArea, string Department);

    /// <summary>Each unit as <c>code — name</c>. The cluster is not stamped on a task; it is the CBU's.</summary>
    private async Task<OrgLabels> OrgLabelsAsync(FieldTask task, Func<string?, string?, string> pick, CancellationToken ct)
    {
        var hierarchy = await scopes.GetHierarchyAsync(ct);
        var clusterCode = hierarchy.ClusterOfCbu(task.CbuCode);

        async Task<string> LabelAsync(string level, string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return Missing;
            }

            var names = await directory.GetUnitNamesAsync(level, [code], ct);
            return names.TryGetValue(code, out var name) ? $"{code} — {pick(name.NameEn, name.NameAr)}" : code;
        }

        return new OrgLabels(
            await LabelAsync(OrgLevels.Cluster, clusterCode),
            await LabelAsync(OrgLevels.Cbu, task.CbuCode),
            await LabelAsync(OrgLevels.Branch, task.BranchCode),
            await LabelAsync(OrgLevels.OperationArea, task.OperationAreaCode),
            await LabelAsync(OrgUnitLevels.Department, task.DepartmentCode));
    }

    /// <summary>
    /// Every file the task's fills carry. Only the latest fill's images are read for embedding —
    /// the answers above are that fill's, and a photo from a fill the reviewer sent back would be
    /// read as evidence for answers it does not belong to.
    /// </summary>
    private async Task<IReadOnlyList<TaskReportFile>> FilesAsync(
        string contextId,
        FormSubmissionRecord? latest,
        IReadOnlyList<FormAnswerView> display,
        Func<string?, string?, string> pick,
        CancellationToken ct)
    {
        var records = await forms.ListFilesByContextAsync(TasksSchema.FormContextType, contextId, ct);
        var fields = display.ToDictionary(a => a.DataName, StringComparer.OrdinalIgnoreCase);

        var files = new List<TaskReportFile>(records.Count);
        var embedded = 0;

        foreach (var record in records.OrderBy(f => f.DataName, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.CreatedAt))
        {
            fields.TryGetValue(record.DataName, out var field);
            var isLatest = latest is not null && record.SubmissionId == latest.SubmissionId;
            var isImage = record.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

            byte[]? bytes = null;
            if (isLatest && isImage && embedded < MaxEmbeddedImages)
            {
                bytes = await forms.ReadContextFileAsync(
                    record.FileId,
                    TasksSchema.FormContextType,
                    contextId,
                    MaxImageBytes,
                    ct);

                if (bytes is not null)
                {
                    embedded++;
                }
            }

            files.Add(new TaskReportFile(
                record.FileName,
                field is null ? record.DataName : (pick(field.LabelEn, field.LabelAr) is { Length: > 0 } label ? label : record.DataName),
                record.ContentType,
                record.SizeBytes,
                record.CreatedAt,
                string.Equals(field?.FieldType, SignatureFieldType, StringComparison.OrdinalIgnoreCase),
                isLatest,
                bytes));
        }

        return files;
    }
}
