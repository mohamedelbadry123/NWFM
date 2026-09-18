namespace Workflow.Application.DTOs;

using Workflow.Domain.Enums;

public sealed record WorkflowRequestDto(
    Guid Id,
    Guid OrganizationId,
    string RequestNumber,
    Guid WorkflowBindingId,
    Guid WorkflowInstanceId,
    string BusinessEntityType,
    string BusinessEntityId,
    string ServiceKey,
    string ServiceNameEn,
    string? ServiceNameAr,
    string? ScreenKey,
    string TriggerEventKey,
    DateTime RequestDate,
    Guid? RequesterUserId,
    WorkflowInstanceStatus Status,
    Guid? CurrentActivityInstanceId,
    string? CurrentActivityNameEn,
    string? CurrentActivityNameAr,
    Guid? OriginalAssignedGroupId,
    string? OriginalAssignedGroupName,
    Guid? CurrentAssignedGroupId,
    string? CurrentAssignedGroupName,
    int? CurrentTaskSlaMinutes,
    DateTime? CurrentTaskDueAtUtc,
    int? RemainingSlaMinutes,
    DateTime? CompletedAtUtc,
    string? CorrelationId,
    Guid? CurrentClaimedByUserId,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record WorkflowRequestKpiDto(
    int Total,
    int InProgress,
    int Completed,
    int Breached);
