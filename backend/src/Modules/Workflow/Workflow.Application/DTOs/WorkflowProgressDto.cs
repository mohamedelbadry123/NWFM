namespace Workflow.Application.DTOs;

public sealed record WorkflowProgressDto(
    WorkflowInstanceDto Instance,
    IReadOnlyList<ActivityInstanceDto> Activities,
    IReadOnlyList<WorkflowEventDto> Events);
