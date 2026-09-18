namespace Workflow.Application.Commands.IgnoreWorkflowIncident;

using MediatR;
using NWFM.Shared.Results;

public sealed record IgnoreWorkflowIncidentCommand(
    Guid IncidentId,
    Guid IgnoredByUserId) : IRequest<Result>;
