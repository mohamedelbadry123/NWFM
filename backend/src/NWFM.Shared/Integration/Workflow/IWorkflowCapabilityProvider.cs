namespace NWFM.Shared.Integration.Workflow;

/// <summary>
/// Module-owned capability catalog for workflow designer dropdowns.
/// Implemented by each business module that opts into Workflow.
/// </summary>
public interface IWorkflowCapabilityProvider
{
    string ModuleKey { get; }

    IReadOnlyList<WorkflowEntityCapability> GetEntities();
}

public sealed record WorkflowEntityCapability(
    string EntityType,
    string NameEn,
    string NameAr,
    IReadOnlyList<WorkflowTriggerDescriptor> Triggers,
    IReadOnlyList<WorkflowFieldDescriptor> Fields);
