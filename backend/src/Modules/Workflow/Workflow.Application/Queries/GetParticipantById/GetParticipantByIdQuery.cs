namespace Workflow.Application.Queries.GetParticipantById;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record GetParticipantByIdQuery(
    Guid ParticipantId,
    Guid OrganizationId) : IRequest<Result<WorkflowParticipantDto>>;
