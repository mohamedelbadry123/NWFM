namespace Workflow.Application.Commands.ResolveWorkflowIncident;

using MediatR;
using NWFM.Shared.Results;

public sealed record ResolveWorkflowIncidentCommand(
    Guid IncidentId,
    Guid ResolvedByUserId,
    string? Notes = null) : IRequest<Result>;
