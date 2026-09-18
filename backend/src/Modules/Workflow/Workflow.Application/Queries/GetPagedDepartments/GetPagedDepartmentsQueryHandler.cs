namespace Workflow.Application.Queries.GetPagedDepartments;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class GetPagedDepartmentsQueryHandler
    : IRequestHandler<GetPagedDepartmentsQuery, Result<PaginatedResult<WorkflowDepartmentDto>>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowDepartmentRepository _repo;

    public GetPagedDepartmentsQueryHandler(
        IWorkflowFeatureGate gate,
        IWorkflowDepartmentRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<PaginatedResult<WorkflowDepartmentDto>>> Handle(
        GetPagedDepartmentsQuery request,
        CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure)
            return Result.Failure<PaginatedResult<WorkflowDepartmentDto>>(gateResult.Error);

        var (items, total) = await _repo.GetPagedAsync(
            request.OrganizationId,
            request.PageNumber,
            request.PageSize,
            request.SearchTerm,
            cancellationToken);

        var dtos = items.Select(d => new WorkflowDepartmentDto(
            d.Id, d.OrganizationId, d.Name, d.NameAr,
            d.Code, d.DefaultAssignmentGroupId, d.IsActive,
            d.Members.Count, d.CreatedAt, d.UpdatedAt)).ToList();

        return Result.Success(new PaginatedResult<WorkflowDepartmentDto>(
            dtos, total, request.PageNumber, request.PageSize));
    }
}
