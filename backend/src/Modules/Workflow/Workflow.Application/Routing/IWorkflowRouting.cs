using NWFM.Shared.Results;
using Workflow.Domain.Entities;

namespace Workflow.Application.Routing;

public sealed record RoutingUpgradePreview(bool CanUpgrade, IReadOnlyList<RoutingDiagnostic> Diagnostics, int ConditionsAdded, string Revision);
public sealed record RoutingUpgradeInput(string ExpectedRevision);
public sealed record RoutingProgress(string NodeKey, string Name, string Status, string Explanation,
    Guid ActivationId, string WaitFor, int Met, int Total, IReadOnlyList<RoutingBranchProgress> Branches,
    IReadOnlyList<string> Skipped, DateTime? DeadlineAt, DateTime? ReleasedAt);
public sealed record RoutingBranchProgress(Guid TokenId, string Name, string Status);
public interface IWorkflowRouting
{
    Task<Result<string>> PrepareAsync(Guid definitionId, Guid versionId, RoutingUpgradeInput input, CancellationToken ct);
    Task<Result<RoutingContext>> ContextAsync(Guid definitionId, Guid versionId, CancellationToken ct);
    Task<Result<RoutingSimulationResult>> SimulateAsync(Guid definitionId, Guid versionId, RoutingSimulationInput input, CancellationToken ct);
    Task<Result<RoutingUpgradePreview>> PreviewUpgradeAsync(Guid definitionId, Guid versionId, CancellationToken ct);
    Task<Result<string>> UpgradeAsync(Guid definitionId, Guid versionId, RoutingUpgradeInput input, CancellationToken ct);
    Task<IReadOnlyList<RoutingDiagnostic>> ValidateAsync(WorkflowVersion version, CancellationToken ct);
    Task<Result<IReadOnlyList<RoutingProgress>>> ProgressAsync(Guid instanceId, CancellationToken ct);
}
