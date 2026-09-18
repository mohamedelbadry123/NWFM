namespace Workflow.Application.Commands.CancelWorkflowInstance;

using MediatR;
using NWFM.Shared.Results;

public sealed record CancelWorkflowInstanceCommand(
    Guid InstanceId,
    Guid OrganizationId,
    Guid ActorUserId) : IRequest<Result<bool>>;
