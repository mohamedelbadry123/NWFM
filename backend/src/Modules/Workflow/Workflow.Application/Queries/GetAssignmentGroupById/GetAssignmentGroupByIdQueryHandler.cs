namespace Workflow.Application.Queries.GetAssignmentGroupById;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class GetAssignmentGroupByIdQueryHandler
    : IRequestHandler<GetAssignmentGroupByIdQuery, Result<WorkflowAssignmentGroupDetailDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowAssignmentGroupRepository _repo;

    public GetAssignmentGroupByIdQueryHandler(
        IWorkflowFeatureGate gate,
        IWorkflowAssignmentGroupRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<WorkflowAssignmentGroupDetailDto>> Handle(
        GetAssignmentGroupByIdQuery request,
        CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure)
            return Result.Failure<WorkflowAssignmentGroupDetailDto>(gateResult.Error);

        var group = await _repo.GetByIdWithMembersAsync(
            request.GroupId, request.OrganizationId, cancellationToken);
        if (group is null)
            return Result.Failure<WorkflowAssignmentGroupDetailDto>(WorkflowErrors.AssignmentGroup.NotFound);

        var memberDtos = group.Members.Select(m => new WorkflowGroupMemberDto(
            m.Id,
            m.ParticipantId,
            m.Participant?.DisplayName ?? string.Empty,
            m.Participant?.DisplayNameAr,
            m.Participant?.Email ?? string.Empty,
            m.CanClaim,
            m.IsPrimary,
            m.ValidFrom,
            m.ValidTo)).ToList();

        return Result.Success(new WorkflowAssignmentGroupDetailDto(
            group.Id, group.OrganizationId, group.Code, group.Name, group.NameAr,
            group.AssignmentStrategy, group.IsActive, memberDtos,
            group.CreatedAt, group.UpdatedAt));
    }
}
