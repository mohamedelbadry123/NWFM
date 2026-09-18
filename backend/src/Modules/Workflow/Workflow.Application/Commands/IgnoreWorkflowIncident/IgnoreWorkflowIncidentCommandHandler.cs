namespace Workflow.Application.Commands.IgnoreWorkflowIncident;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;

public sealed class IgnoreWorkflowIncidentCommandHandler
    : IRequestHandler<IgnoreWorkflowIncidentCommand, Result>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowIncidentService _incidents;

    public IgnoreWorkflowIncidentCommandHandler(
        IWorkflowFeatureGate gate, IWorkflowIncidentService incidents)
    {
        _gate = gate;
        _incidents = incidents;
    }

    public async Task<Result> Handle(
        IgnoreWorkflowIncidentCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return gateResult;

        return await _incidents.IgnoreAsync(
            request.IncidentId, request.IgnoredByUserId, DateTime.UtcNow, cancellationToken);
    }
}
