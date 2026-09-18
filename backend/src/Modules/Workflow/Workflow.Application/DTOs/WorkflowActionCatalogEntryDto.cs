namespace Workflow.Application.DTOs;

public sealed record WorkflowActionCatalogEntryDto(
    string ActionKey,
    string NameEn,
    string NameAr,
    string ModuleKey);
