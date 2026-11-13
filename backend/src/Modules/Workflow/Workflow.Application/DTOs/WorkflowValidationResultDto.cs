namespace Workflow.Application.DTOs;

public sealed record WorkflowValidationResultDto(
    bool IsValid,
    IReadOnlyList<WorkflowValidationIssueDto> Errors,
    IReadOnlyList<WorkflowValidationIssueDto> Warnings);

public sealed record WorkflowValidationIssueDto(
    string Code,
    string Message,
    string? NodeKey = null,
    string? Field = null,
    string Severity = "Error",
    string? ConnectionKey = null)
{
    public string Id => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{Code}|{NodeKey}|{ConnectionKey}|{Field}|{Message}")))[..20];
    public string MessageKey => "workflow.diagnostics." + Code;
    public string CorrectionAction => NodeKey is null ? "ReviewWorkflow" : "FocusField";
}
