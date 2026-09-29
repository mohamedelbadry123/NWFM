namespace NWFM.Shared.Integration.Workflow;

public sealed record WorkflowLookupItem(Guid Id, string Code, string NameEn, string NameAr, string? ParentCode = null);

/// <summary>
/// The department and field-activity lookups a workflow activity is filed under. Implemented by the
/// module that owns them. Territory is not here: a workflow's location uses the shared org hierarchy
/// (<see cref="NWFM.Shared.Integration.Organization.IOrgDirectory"/>).
/// </summary>
public interface IWorkflowReferenceData
{
    Task<IReadOnlyList<WorkflowLookupItem>> ListAsync(string kind, string? parentCode, CancellationToken ct);
    Task<bool> IsValidFieldActivityAsync(string departmentCode, string fieldActivityCode, CancellationToken ct);
}
