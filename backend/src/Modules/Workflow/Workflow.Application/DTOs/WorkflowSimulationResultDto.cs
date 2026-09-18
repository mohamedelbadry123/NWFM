namespace Workflow.Application.DTOs;

public sealed record WorkflowSimulationResultDto(
    IReadOnlyList<WorkflowSimulationStepDto> Steps,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record WorkflowSimulationStepDto(
    string NodeKey,
    string ActivityType,
    string Note);
