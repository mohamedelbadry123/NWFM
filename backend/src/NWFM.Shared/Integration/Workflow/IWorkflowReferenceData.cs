namespace NWFM.Shared.Integration.Workflow;

public sealed record WorkflowLookupItem(Guid Id, string Code, string NameEn, string NameAr, string? ParentCode = null);
public sealed record WorkflowGeography(string ClusterCode, string RegionCode, string CityCode);

/// <summary>Reference-data boundary. Implemented by the module that owns organizational lookups.</summary>
public interface IWorkflowReferenceData
{
    Task<IReadOnlyList<WorkflowLookupItem>> ListAsync(string kind, string? parentCode, CancellationToken ct);
    Task<bool> IsValidGeographyAsync(WorkflowGeography geography, CancellationToken ct);
    Task<bool> IsValidFieldActivityAsync(string departmentCode, string fieldActivityCode, CancellationToken ct);
}
