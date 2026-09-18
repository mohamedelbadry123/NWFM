namespace Workflow.Application.Commands.RemoveDepartmentMember;

using MediatR;
using NWFM.Shared.Results;

public sealed record RemoveDepartmentMemberCommand(
    Guid DepartmentId,
    Guid OrganizationId,
    Guid ParticipantId) : IRequest<Result>;
