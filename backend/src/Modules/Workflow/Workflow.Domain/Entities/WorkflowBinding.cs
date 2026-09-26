namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using NWFM.Shared.MultiTenancy;
using Workflow.Domain.Enums;

/// <summary>
/// Links an organization-owned WorkflowDefinition to the screen/module that starts it.
/// Bindings are design-time configuration — they do not start instances.
/// Mode=Disabled is always safe. Shadow/Active activation requires a published version
/// whose UserTasks resolve to groups in this same organization.
/// </summary>
public sealed class WorkflowBinding : Entity, ITenantAware
{
    public Guid WorkflowDefinitionId { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string ModuleKey { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public string TriggerEvent { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public WorkflowBindingMode Mode { get; private set; }
    public WorkflowVersionPolicy VersionPolicy { get; private set; }
    public WorkflowExecutionPolicy ExecutionPolicy { get; private set; }
    public Guid? FixedWorkflowVersionId { get; private set; }
    public string? StartEventKey { get; private set; }
    public string? StartConditionExpression { get; private set; }

    /// <summary>SaaS link to UI screen, e.g. "consent-requests.create".</summary>
    public string? ScreenKey { get; private set; }
    public string? InputMappingJson { get; private set; }
    public string? OutcomeMappingJson { get; private set; }

    /// <summary>Structured start conditions (JSON).</summary>
    public string? ConditionJson { get; private set; }

    public bool IsActive { get; private set; }
    public bool IsDemo { get; private set; }
    public void MarkDemo() => IsDemo = true;
    public byte[] RowVersion { get; private set; } = [];

    private WorkflowBinding() { }

    public static WorkflowBinding Create(
        Guid workflowDefinitionId,
        Guid organizationId,
        string moduleKey,
        string entityType,
        string triggerEvent,
        DateTime createdAt,
        string? description = null,
        WorkflowBindingMode mode = WorkflowBindingMode.Disabled,
        WorkflowVersionPolicy versionPolicy = WorkflowVersionPolicy.Latest,
        WorkflowExecutionPolicy executionPolicy = WorkflowExecutionPolicy.StartNewInstance,
        Guid? fixedWorkflowVersionId = null,
        string? startEventKey = null,
        string? startConditionExpression = null,
        string? screenKey = null,
        string? inputMappingJson = null,
        string? outcomeMappingJson = null,
        string? conditionJson = null)
    {
        return new WorkflowBinding
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = workflowDefinitionId,
            OrganizationId = organizationId,
            ModuleKey = moduleKey,
            EntityType = entityType,
            TriggerEvent = triggerEvent,
            Description = description,
            Mode = mode,
            VersionPolicy = versionPolicy,
            ExecutionPolicy = executionPolicy,
            FixedWorkflowVersionId = fixedWorkflowVersionId,
            StartEventKey = startEventKey,
            StartConditionExpression = startConditionExpression,
            ScreenKey = screenKey,
            InputMappingJson = inputMappingJson,
            OutcomeMappingJson = outcomeMappingJson,
            ConditionJson = conditionJson,
            IsActive = false,
            CreatedAt = createdAt
        };
    }

    /// <summary>
    /// Legacy update path — preserves ScreenKey / mapping JSON fields.
    /// </summary>
    public void Update(
        string moduleKey,
        string entityType,
        string triggerEvent,
        string? description,
        WorkflowBindingMode mode,
        WorkflowVersionPolicy versionPolicy,
        Guid? fixedWorkflowVersionId,
        string? startEventKey,
        string? startConditionExpression,
        DateTime updatedAt)
    {
        Update(
            moduleKey, entityType, triggerEvent, description, mode, versionPolicy,
            fixedWorkflowVersionId, startEventKey, startConditionExpression, updatedAt,
            ScreenKey, InputMappingJson, OutcomeMappingJson, ConditionJson, ExecutionPolicy);
    }

    public void Update(
        string moduleKey,
        string entityType,
        string triggerEvent,
        string? description,
        WorkflowBindingMode mode,
        WorkflowVersionPolicy versionPolicy,
        Guid? fixedWorkflowVersionId,
        string? startEventKey,
        string? startConditionExpression,
        DateTime updatedAt,
        string? screenKey,
        string? inputMappingJson = null,
        string? outcomeMappingJson = null,
        string? conditionJson = null,
        WorkflowExecutionPolicy? executionPolicy = null)
    {
        ModuleKey = moduleKey;
        EntityType = entityType;
        TriggerEvent = triggerEvent;
        Description = description;
        Mode = mode;
        VersionPolicy = versionPolicy;
        FixedWorkflowVersionId = fixedWorkflowVersionId;
        StartEventKey = startEventKey;
        StartConditionExpression = startConditionExpression;
        ScreenKey = screenKey;
        InputMappingJson = inputMappingJson;
        OutcomeMappingJson = outcomeMappingJson;
        ConditionJson = conditionJson;
        if (executionPolicy.HasValue)
            ExecutionPolicy = executionPolicy.Value;
        SetUpdated(updatedAt);
    }

    public void Deactivate(DateTime updatedAt)
    {
        IsActive = false;
        Mode = WorkflowBindingMode.Disabled;
        SetUpdated(updatedAt);
    }

    public void Activate(DateTime updatedAt)
    {
        IsActive = true;
        SetUpdated(updatedAt);
    }
}
