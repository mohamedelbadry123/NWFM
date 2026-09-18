namespace Workflow.Application.Queries.GetPagedParticipants;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class GetPagedParticipantsQueryHandler
    : IRequestHandler<GetPagedParticipantsQuery, Result<PaginatedResult<WorkflowParticipantDto>>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowParticipantRepository _repo;

    public GetPagedParticipantsQueryHandler(
        IWorkflowFeatureGate gate,
        IWorkflowParticipantRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<PaginatedResult<WorkflowParticipantDto>>> Handle(
        GetPagedParticipantsQuery request,
        CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure)
            return Result.Failure<PaginatedResult<WorkflowParticipantDto>>(gateResult.Error);

        var (items, total) = await _repo.GetPagedAsync(
            request.OrganizationId,
            request.PageNumber,
            request.PageSize,
            request.SearchTerm,
            cancellationToken);

        var dtos = items.Select(p => new WorkflowParticipantDto(
            p.Id, p.OrganizationId, p.UserId, p.DisplayName, p.DisplayNameAr,
            p.Email, p.EmployeeNumber, p.IsActive, p.CreatedAt, p.UpdatedAt)).ToList();

        return Result.Success(new PaginatedResult<WorkflowParticipantDto>(
            dtos, total, request.PageNumber, request.PageSize));
    }
}
