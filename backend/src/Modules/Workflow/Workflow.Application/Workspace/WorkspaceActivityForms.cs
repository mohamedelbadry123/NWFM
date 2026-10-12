using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Results;

namespace Workflow.Application.Workspace;

/// <summary>One published version of an activity's form, frozen as it was published, for a read-only preview.</summary>
public sealed record ActivityFormPreview(Guid FormId, string Code, string NameEn, string NameAr, string Status, int VersionNo, bool IsUsable, string SchemaJson);

/// <summary>
/// Discovers and previews forms associated with an activity's Task Type. Legacy classification lookups remain
/// available for compatibility. FormEngine owns schemas; reads go through <see cref="IFormGateway"/>.
/// </summary>
public interface IWorkspaceActivityForms
{
    Task<Result<FieldActivityFormPage>> ListForTaskTypeAsync(Guid taskTypeId, CancellationToken ct);
    Task<Result<ActivityFormPreview>> PreviewForTaskTypeAsync(Guid taskTypeId, Guid formId, int versionNo, CancellationToken ct);
    Task<Result<FieldActivityFormPage>> ListAsync(string? departmentCode, string? fieldActivityCode, int pageNumber, int pageSize, CancellationToken ct);
    Task<Result<ActivityFormPreview>> PreviewAsync(string? departmentCode, string? fieldActivityCode, Guid formId, int versionNo, CancellationToken ct);
}

public sealed class WorkspaceActivityForms(IFormGateway forms, IWorkflowTaskTypeCatalog? taskTypes = null) : IWorkspaceActivityForms
{
    public async Task<Result<FieldActivityFormPage>> ListForTaskTypeAsync(Guid taskTypeId, CancellationToken ct)
    {
        if (taskTypes is null || !await taskTypes.IsActiveAsync(taskTypeId, ct))
            return Result.Failure<FieldActivityFormPage>(new Error("ActivityForms.TaskType", "Select an active Task Type."));
        var ids = await taskTypes.FormIdsAsync(taskTypeId, ct);
        var found = await forms.ListByIdsAsync(ids, ct);
        var items = ids.Select(id => found.FirstOrDefault(f => f.Id == id)).OfType<FieldActivityFormInfo>().ToList();
        return Result.Success(new FieldActivityFormPage(items, items.Count, items.Count(f => f.IsUsable), 1, MaxPageSize));
    }

    public async Task<Result<ActivityFormPreview>> PreviewForTaskTypeAsync(Guid taskTypeId, Guid formId, int versionNo, CancellationToken ct)
    {
        var page = await ListForTaskTypeAsync(taskTypeId, ct);
        if (page.IsFailure) return Result.Failure<ActivityFormPreview>(page.Error);
        var form = page.Value.Items.FirstOrDefault(f => f.Id == formId);
        if (form is null) return Result.Failure<ActivityFormPreview>(NotFound);
        if (!form.VersionNos.Contains(versionNo)) return Result.Failure<ActivityFormPreview>(VersionNotFound);
        var schema = await forms.GetVersionSchemaAsync(formId, versionNo, ct);
        return schema is null ? Result.Failure<ActivityFormPreview>(VersionNotFound)
            : Result.Success(new ActivityFormPreview(form.Id, form.Code, form.NameEn, form.NameAr, form.Status, versionNo, form.IsUsable, schema));
    }
    public const int MaxPageSize = 100;
    public static readonly Error ContextRequired = new("ActivityForms.ContextRequired", "Select a Department and Field Activity Type.");
    public static readonly Error NotFound = new("ActivityForms.NotFound", "This form is not filed under the activity's Department and Field Activity Type.");
    public static readonly Error VersionNotFound = new("ActivityForms.VersionNotFound", "This form has no such published version.");

    public async Task<Result<FieldActivityFormPage>> ListAsync(string? departmentCode, string? fieldActivityCode, int pageNumber, int pageSize, CancellationToken ct)
    {
        // Both codes, or nothing: a half-filled context must never widen to the whole catalog.
        if (string.IsNullOrWhiteSpace(departmentCode) || string.IsNullOrWhiteSpace(fieldActivityCode)) return Result.Failure<FieldActivityFormPage>(ContextRequired);
        if (pageNumber < 1 || pageSize < 1 || pageSize > MaxPageSize)
            return Result.Failure<FieldActivityFormPage>(Error.Validation("pageSize", $"Page number must be positive and page size between 1 and {MaxPageSize}."));
        return Result.Success(await forms.ListForFieldActivityAsync(departmentCode.Trim(), fieldActivityCode.Trim(), pageNumber, pageSize, ct));
    }

    public async Task<Result<ActivityFormPreview>> PreviewAsync(string? departmentCode, string? fieldActivityCode, Guid formId, int versionNo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(departmentCode) || string.IsNullOrWhiteSpace(fieldActivityCode)) return Result.Failure<ActivityFormPreview>(ContextRequired);
        // The client's filtering is not trusted: the form must really be filed under this combination.
        var form = await forms.FindFieldActivityFormAsync(formId, ct);
        if (form is null || !Same(form.DepartmentCode, departmentCode) || !Same(form.FieldActivityCode, fieldActivityCode)) return Result.Failure<ActivityFormPreview>(NotFound);
        if (!form.VersionNos.Contains(versionNo)) return Result.Failure<ActivityFormPreview>(VersionNotFound);
        var schema = await forms.GetVersionSchemaAsync(formId, versionNo, ct);
        return schema is null
            ? Result.Failure<ActivityFormPreview>(VersionNotFound)
            : Result.Success(new ActivityFormPreview(form.Id, form.Code, form.NameEn, form.NameAr, form.Status, versionNo, form.IsUsable, schema));
    }

    private static bool Same(string stored, string requested) => string.Equals(stored.Trim(), requested.Trim(), StringComparison.OrdinalIgnoreCase);
}
