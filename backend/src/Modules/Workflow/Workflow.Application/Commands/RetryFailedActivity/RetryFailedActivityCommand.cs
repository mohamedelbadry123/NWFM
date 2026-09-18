namespace Workflow.Application.Commands.RetryFailedActivity;

using MediatR;
using NWFM.Shared.Results;

public sealed record RetryFailedActivityCommand(
    Guid InstanceId,
    Guid OrganizationId,
    Guid ActorUserId) : IRequest<Result<bool>>;
