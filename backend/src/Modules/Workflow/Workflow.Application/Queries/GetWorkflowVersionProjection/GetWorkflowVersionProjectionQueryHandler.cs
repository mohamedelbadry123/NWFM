namespace Workflow.Application.Queries.GetWorkflowVersionProjection;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Application.Queries.GetWorkflowVersionById;
using Workflow.Domain.Repositories;

public sealed class GetWorkflowVersionProjectionQueryHandler
    : IRequestHandler<GetWorkflowVersionProjectionQuery, Result<WorkflowVersionDetailDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowVersionRepository _versionRepo;

    public GetWorkflowVersionProjectionQueryHandler(
        IWorkflowFeatureGate gate, IWorkflowVersionRepository versionRepo)
    {
        _gate = gate;
        _versionRepo = versionRepo;
    }

    public async Task<Result<WorkflowVersionDetailDto>> Handle(
        GetWorkflowVersionProjectionQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowVersionDetailDto>(gateResult.Error);

        var version = await _versionRepo.GetByIdWithProjectionAsync(
            request.VersionId, cancellationToken);
        if (version is null)
            return Result.Failure<WorkflowVersionDetailDto>(WorkflowErrors.Version.NotFound);

        return Result.Success(GetWorkflowVersionByIdQueryHandler.MapToDetailDto(version));
    }
}
