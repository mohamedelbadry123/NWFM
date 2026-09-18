namespace Workflow.Application.DTOs;

public sealed record WorkflowValidationResultDto(
    bool IsValid,
    IReadOnlyList<WorkflowValidationIssueDto> Errors,
    IReadOnlyList<WorkflowValidationIssueDto> Warnings);

public sealed record WorkflowValidationIssueDto(
    string Code,
    string Message,
    string? NodeKey = null);
