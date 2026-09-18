namespace Workflow.Application.Commands.SuspendWorkflowInstance;

using MediatR;
using NWFM.Shared.Results;

public sealed record SuspendWorkflowInstanceCommand(
    Guid InstanceId,
    Guid OrganizationId,
    Guid ActorUserId) : IRequest<Result<bool>>;
