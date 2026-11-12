using NWFM.Shared.Integration.Workflow;

namespace Workflow.Application.Workspace;

public sealed record WorkspaceLibraryQuery(int Page = 1, int PageSize = 10, string? Search = null,
    string Status = "Active", string? Scope = null, string Sort = "Updated");
public sealed record WorkspaceLibraryScope(string Level, string Code, string ClusterCode, string? CbuCode,
    string? NameEn = null, string? NameAr = null);
public sealed record WorkspaceLibraryItem(Guid Id, string DefinitionKey, string Name, string? NameAr,
    string? Description, string? DescriptionAr, bool IsActive, DateTime CreatedAt, DateTime UpdatedAt,
    int VersionCount, Guid? VersionId, int? VersionNumber, string VersionStatus, string Kind,
    Guid? ParentDefinitionId, string? ParentNodeKey, WorkflowLookupItem? Classification,
    int ActivityCount, IReadOnlyList<WorkspaceLibraryScope> Scopes, bool InheritsScope,
    bool NeedsAttention, IReadOnlyList<WorkspaceLibraryItem> Children);
public sealed record WorkspaceLibraryPage(IReadOnlyList<WorkspaceLibraryItem> Items, int TotalGroups,
    int TotalWorkflows, int Page, int PageSize, IReadOnlyList<WorkspaceLibraryScope> Scopes);
