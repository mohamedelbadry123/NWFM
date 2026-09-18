namespace Workflow.Application.Commands.DeleteWorkflowBindingAssignmentMapping;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Domain.Repositories;

public sealed class DeleteWorkflowBindingAssignmentMappingCommandHandler
    : IRequestHandler<DeleteWorkflowBindingAssignmentMappingCommand, Result<bool>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowBindingAssignmentMappingRepository _mappingRepo;

    public DeleteWorkflowBindingAssignmentMappingCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkflowBindingAssignmentMappingRepository mappingRepo)
    {
        _gate = gate;
        _mappingRepo = mappingRepo;
    }

    public async Task<Result<bool>> Handle(
        DeleteWorkflowBindingAssignmentMappingCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<bool>(gateResult.Error);

        var mapping = await _mappingRepo.GetByIdAsync(request.MappingId, cancellationToken);
        if (mapping is null)
            return Result.Failure<bool>(WorkflowErrors.AssignmentMapping.NotFound);

        if (mapping.OrganizationId != request.OrganizationId)
            return Result.Failure<bool>(WorkflowErrors.Forbidden);

        mapping.Deactivate(DateTime.UtcNow);
        await _mappingRepo.SaveChangesAsync(cancellationToken);

        return Result.Success(true);
    }
}
