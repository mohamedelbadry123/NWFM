namespace Workflow.Application.DTOs;

public sealed record WorkflowLiveGraphDto(
    string CurrentNodeKey,
    string InstanceStatus,
    IReadOnlyList<WorkflowLiveGraphNodeDto> Nodes,
    IReadOnlyList<WorkflowLiveGraphEdgeDto> Edges);

public sealed record WorkflowLiveGraphNodeDto(
    string NodeKey,
    string ActivityType,
    string Name,
    double X,
    double Y,
    string RuntimeStatus);

public sealed record WorkflowLiveGraphEdgeDto(
    string FromNodeKey,
    string ToNodeKey,
    bool IsDefault);
