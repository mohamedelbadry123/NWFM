namespace Workflow.Application.DTOs;

using Workflow.Domain.Enums;

public sealed record WorkflowEventDto(
    Guid Id,
    Guid WorkflowInstanceId,
    WorkflowEventType EventType,
    string? ActivityNodeKey,
    Guid? ActorUserId,
    string? PayloadJson,
    DateTime OccurredAt,
    string? ActivityNameEn = null,
    string? ActivityNameAr = null,
    string? ActorName = null,
    string? ActorNameAr = null,
    string? Comment = null,
    string? ActionTaken = null,
    string? AttachmentName = null,
    string? AttachmentUrl = null);
