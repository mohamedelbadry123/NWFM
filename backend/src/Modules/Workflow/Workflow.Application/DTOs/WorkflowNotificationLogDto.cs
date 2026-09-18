namespace Workflow.Application.DTOs;

using Workflow.Domain.Enums;

public sealed record WorkflowNotificationLogDto(
    Guid Id,
    Guid OrganizationId,
    string TemplateKey,
    string Channels,
    WorkflowNotificationLogStatus Status,
    string? CorrelationId,
    DateTime CreatedAt,
    string? VariablesJsonTruncated);

public sealed record WorkflowNotificationLogPageDto(
    IReadOnlyList<WorkflowNotificationLogDto> Items,
    int TotalCount,
    int PageNumber,
    int PageSize);
