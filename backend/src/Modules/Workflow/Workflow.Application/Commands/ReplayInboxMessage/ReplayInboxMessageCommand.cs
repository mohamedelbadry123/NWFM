namespace Workflow.Application.Commands.ReplayInboxMessage;

using MediatR;
using NWFM.Shared.Results;

public sealed record ReplayInboxMessageCommand(
    Guid MessageId,
    Guid OrganizationId)
    : IRequest<Result>;
