namespace Workflow.Application.DTOs;

public sealed record WorkflowDefinitionDto(
    Guid Id,
    Guid OrganizationId,
    string DefinitionKey,
    string Name,
    string? NameAr,
    string? Description,
    string? DescriptionAr,
    bool IsActive,
    int VersionCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);
