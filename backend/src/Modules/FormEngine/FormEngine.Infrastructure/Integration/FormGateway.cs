using FormEngine.Application.Common;
using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Common.Schema;
using FormEngine.Application.Submissions.Common;
using FormEngine.Domain.Constants;
using FormEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Results;
using NWFM.Shared.Storage;

namespace FormEngine.Infrastructure.Integration;

/// <summary>
/// The form engine as other modules see it. Reads go straight to the FormEngine context and the
/// submission store; the one write goes through <see cref="IFormSubmissionService"/>, so a fill
/// arriving from another module is checked exactly as one arriving at the submit endpoint.
///
/// Called in-process by a module that has already authorised the caller for its own action (filling
/// a task), so no form-engine permission is checked here.
/// </summary>
internal sealed class FormGateway(
    IFormEngineDbContext context,
    IFormSubmissionStore submissionStore,
    IFormSubmissionService submissions,
    IFileStorage fileStorage) : IFormGateway
{
    /// <summary>A context's fills are few — one per fill of one task — so they are read in one page.</summary>
    private const int ContextPageSize = 200;

    private const int MaxListTake = 500;

    /// <summary>
    /// Parsed schemas by form and version. A task's fills usually share one version, so describing
    /// them all parses it once. Scoped with the gateway, so a republish is seen by the next request.
    /// </summary>
    private readonly Dictionary<(Guid FormId, int VersionNo), FormSchema?> _schemas = [];

    public async Task<PublishedFormInfo?> FindPublishedAsync(Guid formId, CancellationToken cancellationToken)
    {
        var form = await context.FormDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == formId && f.CurrentVersionNo != null, cancellationToken);

        return form is null ? null : ToInfo(form);
    }

    public async Task<PublishedFormInfo?> FindPublishedByCodeAsync(string code, CancellationToken cancellationToken)
    {
        var trimmed = code.Trim();

        var form = await context.FormDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Code == trimmed && f.CurrentVersionNo != null, cancellationToken);

        return form is null ? null : ToInfo(form);
    }

    public async Task<IReadOnlyList<PublishedFormInfo>> ListPublishedAsync(
        string? search,
        int take,
        CancellationToken cancellationToken)
    {
        var query = context.FormDefinitions
            .AsNoTracking()
            .Where(f => f.CurrentVersionNo != null
                && f.IsActive
                && f.Status != FormStatuses.Deprecated
                && f.Status != FormStatuses.Archived);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(f => f.Code.Contains(term) || f.NameEn.Contains(term) || f.NameAr.Contains(term));
        }

        var forms = await query
            .OrderBy(f => f.Code)
            .Take(Math.Clamp(take, 1, MaxListTake))
            .ToListAsync(cancellationToken);

        return forms.Select(ToInfo).ToList();
    }

    public Task<string?> GetVersionSchemaAsync(Guid formId, int versionNo, CancellationToken cancellationToken) =>
        context.FormVersions
            .AsNoTracking()
            .Where(v => v.FormDefinitionId == formId
                && v.VersionNo == versionNo
                && v.TargetClient == FormTargetClients.Formly)
            .Select(v => v.SchemaJson)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Result<FormSubmitReceipt>> SubmitAsync(FormSubmitRequest request, CancellationToken cancellationToken)
    {
        var result = await submissions.SubmitAsync(
            new FormSubmissionRequest
            {
                FormDefinitionId = request.FormId,
                VersionNo = request.VersionNo,
                ContextType = request.ContextType,
                ContextId = request.ContextId,
                ClientSubmissionId = request.ClientSubmissionId,
                ClientFilledAt = request.ClientFilledAt,
                Answers = request.Answers,
            },
            cancellationToken);

        return result.IsSuccess
            ? Result.Success(new FormSubmitReceipt(result.Value.SubmissionId, result.Value.VersionNo, result.Value.IsReplay))
            : Result.Failure<FormSubmitReceipt>(result.Error);
    }

    public async Task<FormSubmissionRecord?> GetLatestByContextAsync(
        Guid formId,
        string contextType,
        string contextId,
        CancellationToken cancellationToken)
    {
        var table = await FormTableLoader.LoadAsync(context, formId, cancellationToken);
        if (table is null)
        {
            return null;
        }

        var row = await submissionStore.GetLatestByContextAsync(table, contextType, contextId, cancellationToken);

        return row is null ? null : ToRecord(formId, row);
    }

    public async Task<IReadOnlyList<FormSubmissionRecord>> ListByContextAsync(
        Guid formId,
        string contextType,
        string contextId,
        CancellationToken cancellationToken)
    {
        var table = await FormTableLoader.LoadAsync(context, formId, cancellationToken);
        if (table is null)
        {
            return [];
        }

        var (items, _) = await submissionStore.ListAsync(
            table,
            new FormSubmissionListFilter
            {
                PageNumber = 1,
                PageSize = ContextPageSize,
                ContextType = contextType,
                ContextId = contextId,
            },
            cancellationToken);

        return items.Select(row => ToRecord(formId, row)).ToList();
    }

    public async Task<IReadOnlyList<FormFileRecord>> ListFilesByContextAsync(
        string contextType,
        string contextId,
        CancellationToken cancellationToken) =>
        await context.SubmissionFiles
            .AsNoTracking()
            .Where(f => f.ContextType == contextType && f.ContextId == contextId && f.IsActive)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new FormFileRecord(
                f.Id,
                f.FormDefinitionId,
                f.SubmissionId,
                f.DataName,
                f.FileName,
                f.ContentType,
                f.SizeBytes,
                f.Status,
                f.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FormFieldInfo>> GetFieldsAsync(Guid formId, int versionNo, CancellationToken cancellationToken)
    {
        var schema = await SchemaAsync(formId, versionNo, cancellationToken);

        return schema is null
            ? []
            : schema.Fields
                .Select(f => new FormFieldInfo(
                    f.DataName,
                    f.FieldType,
                    f.LabelEn,
                    f.LabelAr,
                    f.C2mParameterName,
                    f.Choices.Select(c => new FormChoiceInfo(c.Value, c.LabelEn, c.LabelAr, c.C2mFaStatus, c.C2mReason)).ToList()))
                .ToList();
    }

    public async Task<IReadOnlyList<FormAnswerView>> DescribeAnswersAsync(
        Guid formId,
        int versionNo,
        IReadOnlyDictionary<string, object?> answers,
        CancellationToken cancellationToken)
    {
        var schema = await SchemaAsync(formId, versionNo, cancellationToken);
        return schema is null ? [] : FormAnswerDescriber.Describe(schema, answers);
    }

    private async Task<FormSchema?> SchemaAsync(Guid formId, int versionNo, CancellationToken cancellationToken)
    {
        if (!_schemas.TryGetValue((formId, versionNo), out var schema))
        {
            var json = await GetVersionSchemaAsync(formId, versionNo, cancellationToken);
            schema = json is null ? null : FormSchemaParser.Parse(json);
            _schemas[(formId, versionNo)] = schema;
        }

        return schema;
    }

    public async Task<byte[]?> ReadContextFileAsync(
        Guid fileId,
        string contextType,
        string contextId,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var file = await context.SubmissionFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(
                f => f.Id == fileId && f.ContextType == contextType && f.ContextId == contextId && f.IsActive,
                cancellationToken);

        if (file is null || file.SizeBytes > maxBytes)
        {
            return null;
        }

        try
        {
            await using var stream = await fileStorage.OpenReadAsync(file.RelativePath, cancellationToken);
            using var buffer = new MemoryStream((int)Math.Max(file.SizeBytes, 0));
            await stream.CopyToAsync(buffer, cancellationToken);
            return buffer.ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The row outlived its bytes; the caller reports the file as unavailable.
            return null;
        }
    }

    private static PublishedFormInfo ToInfo(FormDefinition form) =>
        new(
            form.Id,
            form.Code,
            form.NameEn,
            form.NameAr,
            form.Category,
            form.Status,
            form.CurrentVersionNo ?? 0,
            form.AcceptsSubmissions);

    private static FormSubmissionRecord ToRecord(Guid formId, IReadOnlyDictionary<string, object?> row) =>
        new(
            row.TryGetValue(FormSubmissionColumns.Id, out var id) && id is Guid submissionId ? submissionId : Guid.Empty,
            formId,
            row.TryGetValue(FormSubmissionColumns.VersionNo, out var version) && version is int versionNo ? versionNo : 0,
            row.TryGetValue(FormSubmissionColumns.SubmittedBy, out var by) ? by as string : null,
            row.TryGetValue(FormSubmissionColumns.SubmittedByName, out var byName) ? byName as string : null,
            row.TryGetValue(FormSubmissionColumns.SubmittedDate, out var date) && date is DateTimeOffset submitted ? submitted : null,
            SubmissionRows.AnswersOf(row));
}
