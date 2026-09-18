namespace Workflow.Application.Commands.RemoveGroupMember;

using MediatR;
using NWFM.Shared.Results;

public sealed record RemoveGroupMemberCommand(
    Guid GroupId,
    Guid OrganizationId,
    Guid ParticipantId) : IRequest<Result>;
