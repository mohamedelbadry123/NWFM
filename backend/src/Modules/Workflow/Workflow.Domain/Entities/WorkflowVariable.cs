namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using NWFM.Shared.MultiTenancy;
using Workflow.Domain.Enums;

/// <summary>
/// Stores the current value of a runtime variable for a WorkflowInstance.
/// Upserted by the engine when a variable is set or updated during execution.
/// </summary>
public sealed class WorkflowVariable : Entity, ITenantAware
{
    public Guid OrganizationId { get; private set; }
    public Guid WorkflowInstanceId { get; private set; }
    public string VariableName { get; private set; } = string.Empty;
    public string? ValueJson { get; private set; }
    public VariableDataType DataType { get; private set; }

    private WorkflowVariable() { }

    public static WorkflowVariable Create(
        Guid organizationId,
        Guid workflowInstanceId,
        string variableName,
        VariableDataType dataType,
        string? valueJson,
        DateTime createdAt)
    {
        return new WorkflowVariable
        {
            Id                 = Guid.NewGuid(),
            OrganizationId     = organizationId,
            WorkflowInstanceId = workflowInstanceId,
            VariableName       = variableName,
            DataType           = dataType,
            ValueJson          = valueJson,
            CreatedAt          = createdAt,
        };
    }

    public void SetValue(string? valueJson, DateTime updatedAt)
    {
        ValueJson = valueJson;
        SetUpdated(updatedAt);
    }
}
