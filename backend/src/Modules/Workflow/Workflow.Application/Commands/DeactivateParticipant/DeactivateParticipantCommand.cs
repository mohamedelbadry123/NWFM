namespace Workflow.Application.Commands.DeactivateParticipant;

using MediatR;
using NWFM.Shared.Results;

public sealed record DeactivateParticipantCommand(
    Guid ParticipantId,
    Guid OrganizationId) : IRequest<Result>;
