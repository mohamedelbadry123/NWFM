namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using Workflow.Domain.Enums;

public sealed class ActivityActionDefinition : Entity
{
    public Guid WorkflowVersionId { get; private set; }
    public Guid ActivityDefinitionId { get; private set; }
    public string ActionKey { get; private set; } = string.Empty;
    public ActionExecutionTrigger ExecutionTrigger { get; private set; }
    public string? OutcomeKey { get; private set; }
    public string? ConditionExpression { get; private set; }
    public int Sequence { get; private set; }
    public string? InputMappingJson { get; private set; }
    public string? OutputMappingJson { get; private set; }
    public ActionFailurePolicy FailurePolicy { get; private set; }
    public int RetryCount { get; private set; }
    public int RetryDelaySeconds { get; private set; }
    public int TimeoutSeconds { get; private set; }
    public bool IsActive { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private ActivityActionDefinition() { }

    public static ActivityActionDefinition Create(
        Guid workflowVersionId,
        Guid activityDefinitionId,
        string actionKey,
        ActionExecutionTrigger executionTrigger,
        int sequence,
        DateTime createdAt,
        string? outcomeKey = null,
        string? conditionExpression = null,
        string? inputMappingJson = null,
        string? outputMappingJson = null,
        ActionFailurePolicy failurePolicy = ActionFailurePolicy.Continue,
        int retryCount = 0,
        int retryDelaySeconds = 0,
        int timeoutSeconds = 0)
    {
        return new ActivityActionDefinition
        {
            Id = Guid.NewGuid(),
            WorkflowVersionId = workflowVersionId,
            ActivityDefinitionId = activityDefinitionId,
            ActionKey = actionKey,
            ExecutionTrigger = executionTrigger,
            OutcomeKey = outcomeKey,
            ConditionExpression = conditionExpression,
            Sequence = sequence,
            InputMappingJson = inputMappingJson,
            OutputMappingJson = outputMappingJson,
            FailurePolicy = failurePolicy,
            RetryCount = retryCount,
            RetryDelaySeconds = retryDelaySeconds,
            TimeoutSeconds = timeoutSeconds,
            IsActive = true,
            CreatedAt = createdAt
        };
    }
}
