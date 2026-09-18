namespace Workflow.Application.Commands.AddGroupMember;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record AddGroupMemberCommand(
    Guid GroupId,
    Guid OrganizationId,
    Guid ParticipantId,
    bool CanClaim,
    bool IsPrimary,
    DateTime? ValidFrom,
    DateTime? ValidTo) : IRequest<Result<WorkflowGroupMemberDto>>;
