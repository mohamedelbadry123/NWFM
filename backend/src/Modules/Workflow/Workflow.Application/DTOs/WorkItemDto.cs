namespace Workflow.Application.DTOs;

using Workflow.Domain.Enums;

public sealed record WorkItemDto(
    Guid Id,
    Guid WorkflowInstanceId,
    Guid ActivityInstanceId,
    Guid OrganizationId,
    Guid AssignmentGroupId,
    string? AssignmentGroupName,
    Guid? ClaimedByUserId,
    DateTime? ClaimedAt,
    Guid? CompletedByUserId,
    DateTime? CompletedAt,
    DateTime? DueAt,
    WorkItemStatus Status,
    string? ActionTaken,
    string? CommentText,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? RequestNumber = null,
    string? ServiceNameEn = null,
    string? ServiceNameAr = null,
    DateTime? RequestDate = null,
    string? CurrentStepNameEn = null,
    string? CurrentStepNameAr = null,
    int? SlaDurationMinutes = null,
    int? RemainingSlaMinutes = null,
    string? OriginalGroupName = null,
    IReadOnlyList<ActivityOutcomeDefinitionDto>? AvailableOutcomes = null);
