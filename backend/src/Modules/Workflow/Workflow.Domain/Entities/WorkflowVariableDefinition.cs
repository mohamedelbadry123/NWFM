namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using Workflow.Domain.Enums;

public sealed class WorkflowVariableDefinition : Entity
{
    public Guid WorkflowVersionId { get; private set; }
    public string VariableKey { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? NameAr { get; private set; }
    public VariableDataType DataType { get; private set; }
    public bool IsRequired { get; private set; }
    public string? DefaultValue { get; private set; }
    public bool IsSensitive { get; private set; }
    public string? Description { get; private set; }
    public string? DescriptionAr { get; private set; }

    private WorkflowVariableDefinition() { }

    public static WorkflowVariableDefinition Create(
        Guid workflowVersionId,
        string variableKey,
        string name,
        VariableDataType dataType,
        DateTime createdAt,
        string? nameAr = null,
        bool isRequired = false,
        bool isSensitive = false,
        string? defaultValue = null,
        string? description = null,
        string? descriptionAr = null)
    {
        return new WorkflowVariableDefinition
        {
            Id = Guid.NewGuid(),
            WorkflowVersionId = workflowVersionId,
            VariableKey = variableKey,
            Name = name,
            NameAr = nameAr,
            DataType = dataType,
            IsRequired = isRequired,
            IsSensitive = isSensitive,
            DefaultValue = defaultValue,
            Description = description,
            DescriptionAr = descriptionAr,
            CreatedAt = createdAt
        };
    }
}
