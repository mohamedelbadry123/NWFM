namespace Workflow.Application.DTOs;

public sealed record ActivityOutcomeDefinitionDto(
    Guid Id,
    Guid ActivityDefinitionId,
    string OutcomeKey,
    string Name,
    string? NameAr,
    string? Description,
    string? DescriptionAr,
    int SortOrder,
    bool RequiresComment,
    bool RequiresAttachment,
    bool IsDefault,
    bool IsActive,
    string? ResultValue);
