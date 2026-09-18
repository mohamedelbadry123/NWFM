namespace Workflow.Application.Commands.ReleaseWorkItem;

using MediatR;
using NWFM.Shared.Results;

public sealed record ReleaseWorkItemCommand(
    Guid WorkItemId,
    Guid UserId,
    Guid OrganizationId) : IRequest<Result<bool>>;
