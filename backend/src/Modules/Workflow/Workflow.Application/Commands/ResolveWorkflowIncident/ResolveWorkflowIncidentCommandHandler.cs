namespace Workflow.Application.Commands.ResolveWorkflowIncident;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;

public sealed class ResolveWorkflowIncidentCommandHandler
    : IRequestHandler<ResolveWorkflowIncidentCommand, Result>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowIncidentService _incidents;

    public ResolveWorkflowIncidentCommandHandler(
        IWorkflowFeatureGate gate, IWorkflowIncidentService incidents)
    {
        _gate = gate;
        _incidents = incidents;
    }

    public async Task<Result> Handle(
        ResolveWorkflowIncidentCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return gateResult;

        return await _incidents.ResolveAsync(
            request.IncidentId, request.ResolvedByUserId, DateTime.UtcNow,
            request.Notes, cancellationToken);
    }
}
