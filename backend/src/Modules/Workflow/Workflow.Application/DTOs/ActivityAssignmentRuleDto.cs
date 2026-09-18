namespace Workflow.Application.DTOs;

using Workflow.Domain.Enums;

public sealed record ActivityAssignmentRuleDto(
    Guid Id,
    Guid ActivityDefinitionId,
    AssigneeType AssigneeType,
    string? AssignmentKey,
    Guid? ReferenceId,
    string? Expression,
    int Priority,
    bool IsFallback,
    bool IsActive,
    string? AssignmentPurpose = null);
