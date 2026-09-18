namespace NWFM.Shared.Integration.Workflow;

public sealed record WorkflowActionExecutionContext(
    Guid OrganizationId,
    Guid WorkflowInstanceId,
    string ActionKey,
    IReadOnlyDictionary<string, object?> InputVariables,
    string IdempotencyKey);
