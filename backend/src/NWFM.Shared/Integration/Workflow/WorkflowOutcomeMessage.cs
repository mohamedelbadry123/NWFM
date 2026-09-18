namespace NWFM.Shared.Integration.Workflow;

public sealed record WorkflowOutcomeMessage(
    Guid OrganizationId,
    Guid BindingId,
    Guid WorkflowInstanceId,
    string ModuleKey,
    string BusinessEntityType,
    string BusinessEntityId,
    string OutcomeKey,
    string CorrelationId,
    IReadOnlyDictionary<string, object?> ApprovedOutputs,
    DateTime OccurredAt,
    string MessageId);
