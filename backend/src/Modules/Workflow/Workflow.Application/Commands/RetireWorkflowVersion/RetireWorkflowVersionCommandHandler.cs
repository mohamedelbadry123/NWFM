namespace Workflow.Application.Commands.RetireWorkflowVersion;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Commands.CreateWorkflowDraft;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

public sealed class RetireWorkflowVersionCommandHandler
    : IRequestHandler<RetireWorkflowVersionCommand, Result<WorkflowVersionDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowVersionRepository _versionRepo;

    public RetireWorkflowVersionCommandHandler(
        IWorkflowFeatureGate gate, IWorkflowVersionRepository versionRepo)
    {
        _gate = gate;
        _versionRepo = versionRepo;
    }

    public async Task<Result<WorkflowVersionDto>> Handle(
        RetireWorkflowVersionCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowVersionDto>(gateResult.Error);

        var version = await _versionRepo.GetByIdAsync(
            request.VersionId, cancellationToken);
        if (version is null)
            return Result.Failure<WorkflowVersionDto>(WorkflowErrors.Version.NotFound);

        if (version.Status == WorkflowVersionStatus.Retired)
            return Result.Failure<WorkflowVersionDto>(WorkflowErrors.Version.AlreadyRetired);

        version.Retire(DateTime.UtcNow);
        await _versionRepo.SaveChangesAsync(cancellationToken);

        return Result.Success(CreateWorkflowDraftCommandHandler.MapToDto(version));
    }
}
