namespace Workflow.Application.DTOs;

using Workflow.Domain.Enums;

/// <summary>
/// Safe ops DTO — no technical stack traces or TechnicalDetailsJson.
/// </summary>
public sealed record WorkflowIncidentDto(
    Guid Id,
    Guid OrganizationId,
    Guid WorkflowInstanceId,
    Guid? ActivityInstanceId,
    string? ActivityNodeKey,
    WorkflowIncidentType IncidentType,
    WorkflowIncidentSeverity Severity,
    WorkflowIncidentStatus Status,
    string Title,
    string? ErrorCode,
    string? ErrorMessage,
    DateTime? ResolvedAt,
    Guid? ResolvedByUserId,
    string? ResolutionNotes,
    DateTime? IgnoredAt,
    Guid? IgnoredByUserId,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>Compact summary for instance progress / related panels.</summary>
public sealed record WorkflowIncidentSummaryDto(
    Guid Id,
    WorkflowIncidentType IncidentType,
    WorkflowIncidentSeverity Severity,
    WorkflowIncidentStatus Status,
    string Title,
    string? ErrorCode,
    DateTime CreatedAt);
