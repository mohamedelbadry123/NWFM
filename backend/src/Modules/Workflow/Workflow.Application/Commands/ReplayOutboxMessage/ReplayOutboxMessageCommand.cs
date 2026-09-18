namespace Workflow.Application.Commands.ReplayOutboxMessage;

using MediatR;
using NWFM.Shared.Results;

public sealed record ReplayOutboxMessageCommand(
    Guid MessageId,
    Guid OrganizationId)
    : IRequest<Result>;
