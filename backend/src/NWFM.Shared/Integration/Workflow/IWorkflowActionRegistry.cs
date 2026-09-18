namespace NWFM.Shared.Integration.Workflow;

/// <summary>
/// Resolves registered workflow actions across all IWorkflowActionProvider implementations.
/// </summary>
public interface IWorkflowActionRegistry
{
    IWorkflowActionProvider? Resolve(string actionKey);
    IReadOnlyList<WorkflowActionDescriptor> GetAll();
}
