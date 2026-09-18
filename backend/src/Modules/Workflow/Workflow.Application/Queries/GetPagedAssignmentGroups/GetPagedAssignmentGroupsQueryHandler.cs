namespace Workflow.Application.Queries.GetPagedAssignmentGroups;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class GetPagedAssignmentGroupsQueryHandler
    : IRequestHandler<GetPagedAssignmentGroupsQuery, Result<PaginatedResult<WorkflowAssignmentGroupDto>>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowAssignmentGroupRepository _repo;

    public GetPagedAssignmentGroupsQueryHandler(
        IWorkflowFeatureGate gate,
        IWorkflowAssignmentGroupRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<PaginatedResult<WorkflowAssignmentGroupDto>>> Handle(
        GetPagedAssignmentGroupsQuery request,
        CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure)
            return Result.Failure<PaginatedResult<WorkflowAssignmentGroupDto>>(gateResult.Error);

        var (items, total) = await _repo.GetPagedAsync(
            request.OrganizationId,
            request.PageNumber,
            request.PageSize,
            request.SearchTerm,
            cancellationToken);

        var dtos = items.Select(g => new WorkflowAssignmentGroupDto(
            g.Id, g.OrganizationId, g.Code, g.Name, g.NameAr,
            g.AssignmentStrategy, g.IsActive,
            g.Members.Count, g.CreatedAt, g.UpdatedAt)).ToList();

        return Result.Success(new PaginatedResult<WorkflowAssignmentGroupDto>(
            dtos, total, request.PageNumber, request.PageSize));
    }
}
