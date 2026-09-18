namespace Workflow.Application.Queries.ListActiveGroupsForOrg;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class ListActiveGroupsForOrgQueryHandler
    : IRequestHandler<ListActiveGroupsForOrgQuery, Result<IReadOnlyList<WorkflowAssignmentGroupDto>>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowAssignmentGroupRepository _repo;

    public ListActiveGroupsForOrgQueryHandler(
        IWorkflowFeatureGate gate, IWorkflowAssignmentGroupRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<IReadOnlyList<WorkflowAssignmentGroupDto>>> Handle(
        ListActiveGroupsForOrgQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure)
            return Result.Failure<IReadOnlyList<WorkflowAssignmentGroupDto>>(gateResult.Error);

        var groups = await _repo.GetActiveGroupsForOrgAsync(request.OrganizationId, cancellationToken);

        var dtos = groups.Select(g => new WorkflowAssignmentGroupDto(
            g.Id, g.OrganizationId, g.Code, g.Name, g.NameAr,
            g.AssignmentStrategy, g.IsActive,
            g.Members.Count, g.CreatedAt, g.UpdatedAt))
            .ToList();

        return Result.Success<IReadOnlyList<WorkflowAssignmentGroupDto>>(dtos);
    }
}
