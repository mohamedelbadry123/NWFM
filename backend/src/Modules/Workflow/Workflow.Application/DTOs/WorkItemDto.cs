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
    IReadOnlyList<ActivityOutcomeDefinitionDto>? AvailableOutcomes = null,
    IReadOnlyList<Workflow.Application.Helpers.WorkflowTaskField>? FormFields = null,
    IReadOnlyDictionary<string, object?>? FormValues = null,
    string? InstructionsEn = null, string? InstructionsAr = null,
    WorkflowSelectedFormDto? SelectedForm = null,
    Guid? FormVisitId = null, string? ActiveFormStepId = null,
    IReadOnlyList<Workflow.Application.Workspace.ActivityFormStep>? FormSteps = null,
    IReadOnlyList<WorkflowFormVisitDto>? FormHistory = null);

public sealed record WorkflowSelectedFormDto(Guid FormId, int VersionNo, string? SchemaJson, Guid? VisitId = null, string? StepId = null);

public sealed record WorkflowFormVisitDto(Guid VisitId, string StepId, Guid FormId, int VersionNo, Guid SubmissionId,
    DateTime SubmittedAt, string? RuleId, Workflow.Application.Workspace.FormRouteDestination Destination,
    IReadOnlyDictionary<string, object?> Answers, string? SchemaJson);
