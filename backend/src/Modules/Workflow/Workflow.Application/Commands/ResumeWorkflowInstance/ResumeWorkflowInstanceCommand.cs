namespace Workflow.Application.Commands.ResumeWorkflowInstance;

using MediatR;
using NWFM.Shared.Results;

public sealed record ResumeWorkflowInstanceCommand(
    Guid InstanceId,
    Guid OrganizationId,
    Guid ActorUserId) : IRequest<Result<bool>>;
