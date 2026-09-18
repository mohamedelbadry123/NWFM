namespace Workflow.Application.DTOs;

using Workflow.Domain.Enums;

public sealed record ActivityDefinitionDto(
    Guid Id,
    Guid WorkflowVersionId,
    string NodeKey,
    ActivityType ActivityType,
    string Name,
    string? NameAr,
    string? ActionKey,
    string? ConfigurationJson,
    double? PositionX,
    double? PositionY,
    IReadOnlyList<ActivityAssignmentRuleDto> AssignmentRules,
    IReadOnlyList<ActivityOutcomeDefinitionDto> Outcomes,
    IReadOnlyList<ActivityActionDefinitionDto> Actions);
