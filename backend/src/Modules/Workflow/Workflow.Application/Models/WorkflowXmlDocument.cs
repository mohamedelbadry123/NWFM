namespace Workflow.Application.Models;

/// <summary>Safe internal representation of a parsed Workflow XML document. Never contains executable content.</summary>
public sealed class WorkflowXmlDocument
{
    public string SchemaVersion { get; init; } = "1.0";
    public IReadOnlyList<ActivityXmlNode> Activities { get; init; } = [];
    public IReadOnlyList<TransitionXmlNode> Transitions { get; init; } = [];
    public IReadOnlyList<VariableXmlNode> Variables { get; init; } = [];
}

public sealed class ActivityXmlNode
{
    public string NodeKey { get; init; } = string.Empty;
    public string ActivityTypeName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? NameAr { get; init; }
    public string? ActionKey { get; init; }
    public string? AssignmentKey { get; init; }
    public string? AssignmentGroupId { get; init; }
    public string? AssignmentPurpose { get; init; }
    public string? ConfigurationJson { get; init; }
    public double? PositionX { get; init; }
    public double? PositionY { get; init; }
    public IReadOnlyList<AssignmentRuleXmlNode> AssignmentRules { get; init; } = [];
    public IReadOnlyList<OutcomeXmlNode> Outcomes { get; init; } = [];
    public IReadOnlyList<ActionXmlNode> Actions { get; init; } = [];
}

public sealed class OutcomeXmlNode
{
    public string OutcomeKey { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? NameAr { get; init; }
    public string? Description { get; init; }
    public string? DescriptionAr { get; init; }
    public int SortOrder { get; init; }
    public bool RequiresComment { get; init; }
    public bool RequiresAttachment { get; init; }
    public bool IsDefault { get; init; }
    public string? ResultValue { get; init; }
}

public sealed class ActionXmlNode
{
    public string ActionKey { get; init; } = string.Empty;
    public string ExecutionTriggerName { get; init; } = string.Empty;
    public string? OutcomeKey { get; init; }
    public string? ConditionExpression { get; init; }
    public int Sequence { get; init; }
    public string? InputMappingJson { get; init; }
    public string? OutputMappingJson { get; init; }
    public string FailurePolicyName { get; init; } = "Continue";
    public int RetryCount { get; init; }
    public int RetryDelaySeconds { get; init; }
    public int TimeoutSeconds { get; init; }
}

public sealed class AssignmentRuleXmlNode
{
    public string AssigneeTypeName { get; init; } = string.Empty;
    public string? AssignmentPurpose { get; init; }
    public string? AssignmentKey { get; init; }
    public string? ReferenceId { get; init; }
    public string? Expression { get; init; }
    public int Priority { get; init; }
    public bool IsFallback { get; init; }
}

public sealed class TransitionXmlNode
{
    public string Key { get; init; } = string.Empty;
    public string FromNodeKey { get; init; } = string.Empty;
    public string ToNodeKey { get; init; } = string.Empty;
    public string? ConditionExpression { get; init; }
    public bool IsDefault { get; init; }
    public int Priority { get; init; }
}

public sealed class VariableXmlNode
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? NameAr { get; init; }
    public string DataTypeName { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
    public bool IsSensitive { get; init; }
    public string? DefaultValue { get; init; }
    public string? Description { get; init; }
    public string? DescriptionAr { get; init; }
}
