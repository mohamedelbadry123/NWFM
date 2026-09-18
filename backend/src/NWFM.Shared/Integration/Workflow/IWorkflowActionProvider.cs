namespace NWFM.Shared.Integration.Workflow;

/// <summary>
/// Module-owned provider that exposes service-task / system actions the Workflow engine can invoke.
/// </summary>
public interface IWorkflowActionProvider
{
    IReadOnlyList<WorkflowActionDescriptor> GetDescriptors();

    Task<WorkflowActionExecutionResult> ExecuteAsync(
        WorkflowActionExecutionContext context,
        CancellationToken cancellationToken = default);
}
