namespace Workflow.Application.Abstractions;

using NWFM.Shared.Results;

/// <summary>
/// Centralized gate that every Workflow command/query handler must check before executing.
/// Returns <see cref="Results.Error.Code"/> = "Workflow.ModuleDisabled" when the feature is off.
/// Never scatter raw IOptions&lt;WorkflowSettings&gt; checks in individual handlers.
/// </summary>
public interface IWorkflowFeatureGate
{
    /// <summary>
    /// Returns <see cref="Result.Success()"/> when the module is enabled,
    /// or <see cref="Result.Failure"/> with <c>Workflow.ModuleDisabled</c> when it is not.
    /// </summary>
    Result EnsureEnabled();
}
