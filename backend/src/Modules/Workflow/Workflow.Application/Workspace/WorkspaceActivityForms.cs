using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Results;

namespace Workflow.Application.Workspace;

/// <summary>One published version of an activity's form, frozen as it was published, for a read-only preview.</summary>
public sealed record ActivityFormPreview(Guid FormId, string Code, string NameEn, string NameAr, string Status, int VersionNo, bool IsUsable, string SchemaJson);

/// <summary>
/// The Form Engine forms an activity's Department + Field Activity Type are filed under, as the designer's Form tab shows
/// them. Discovery only: nothing here binds a form to the activity, and the form engine stays the owner of the forms —
/// they are read through <see cref="IFormGateway"/>, never from its tables.
/// </summary>
public interface IWorkspaceActivityForms
{
    Task<Result<FieldActivityFormPage>> ListAsync(string? departmentCode, string? fieldActivityCode, int pageNumber, int pageSize, CancellationToken ct);
    Task<Result<ActivityFormPreview>> PreviewAsync(string? departmentCode, string? fieldActivityCode, Guid formId, int versionNo, CancellationToken ct);
}

public sealed class WorkspaceActivityForms(IFormGateway forms) : IWorkspaceActivityForms
{
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
