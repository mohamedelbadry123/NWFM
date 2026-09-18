namespace Workflow.Application.Commands.AddDepartmentMember;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record AddDepartmentMemberCommand(
    Guid DepartmentId,
    Guid OrganizationId,
    Guid ParticipantId) : IRequest<Result<WorkflowDepartmentMemberDto>>;
