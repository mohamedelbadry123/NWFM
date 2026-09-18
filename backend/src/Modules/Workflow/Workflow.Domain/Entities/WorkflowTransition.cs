namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;

public sealed class WorkflowTransition : Entity
{
    public Guid WorkflowVersionId { get; private set; }
    public Guid FromActivityDefinitionId { get; private set; }
    public Guid ToActivityDefinitionId { get; private set; }
    public string TransitionKey { get; private set; } = string.Empty;
    public string? ConditionExpression { get; private set; }
    public bool IsDefault { get; private set; }
    public int Priority { get; private set; }

    private WorkflowTransition() { }

    public static WorkflowTransition Create(
        Guid workflowVersionId,
        Guid fromActivityDefinitionId,
        Guid toActivityDefinitionId,
        string transitionKey,
        int priority,
        DateTime createdAt,
        bool isDefault = false,
        string? conditionExpression = null)
    {
        return new WorkflowTransition
        {
            Id = Guid.NewGuid(),
            WorkflowVersionId = workflowVersionId,
            FromActivityDefinitionId = fromActivityDefinitionId,
            ToActivityDefinitionId = toActivityDefinitionId,
            TransitionKey = transitionKey,
            ConditionExpression = conditionExpression,
            IsDefault = isDefault,
            Priority = priority,
            CreatedAt = createdAt
        };
    }
}
