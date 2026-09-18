namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using Workflow.Domain.Enums;

public sealed class ActivityDefinition : Entity
{
    public Guid WorkflowVersionId { get; private set; }
    public string NodeKey { get; private set; } = string.Empty;
    public ActivityType ActivityType { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? NameAr { get; private set; }
    public string? ActionKey { get; private set; }
    public string? ConfigurationJson { get; private set; }
    public double? PositionX { get; private set; }
    public double? PositionY { get; private set; }

    private readonly List<ActivityAssignmentRule> _assignmentRules = [];
    private readonly List<ActivityOutcomeDefinition> _outcomes = [];
    private readonly List<ActivityActionDefinition> _actions = [];

    public IReadOnlyList<ActivityAssignmentRule> AssignmentRules => _assignmentRules.AsReadOnly();
    public IReadOnlyList<ActivityOutcomeDefinition> Outcomes => _outcomes.AsReadOnly();
    public IReadOnlyList<ActivityActionDefinition> Actions => _actions.AsReadOnly();

    private ActivityDefinition() { }

    public static ActivityDefinition Create(
        Guid workflowVersionId,
        string nodeKey,
        ActivityType activityType,
        string name,
        DateTime createdAt,
        string? nameAr = null,
        string? actionKey = null,
        string? configurationJson = null,
        double? positionX = null,
        double? positionY = null)
    {
        return new ActivityDefinition
        {
            Id = Guid.NewGuid(),
            WorkflowVersionId = workflowVersionId,
            NodeKey = nodeKey,
            ActivityType = activityType,
            Name = name,
            NameAr = nameAr,
            ActionKey = actionKey,
            ConfigurationJson = configurationJson,
            PositionX = positionX,
            PositionY = positionY,
            CreatedAt = createdAt
        };
    }
}
