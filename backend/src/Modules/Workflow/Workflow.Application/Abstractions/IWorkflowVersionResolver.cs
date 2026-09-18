namespace Workflow.Application.Abstractions;

using NWFM.Shared.Results;
using Workflow.Domain.Entities;

/// <summary>
/// Resolves which Published WorkflowVersion to pin when starting a new instance.
/// Respects the binding's VersionPolicy: Latest returns the highest published version number;
/// Fixed returns the specific version referenced by FixedWorkflowVersionId.
/// Fails if the resolved version is not in Published status — Draft is never executed.
/// </summary>
public interface IWorkflowVersionResolver
{
    Task<Result<WorkflowVersion>> ResolveAsync(
        WorkflowBinding binding,
        CancellationToken cancellationToken = default);
}
