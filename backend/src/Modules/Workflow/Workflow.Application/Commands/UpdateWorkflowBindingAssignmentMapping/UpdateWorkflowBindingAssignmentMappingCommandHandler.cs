namespace Workflow.Application.Commands.UpdateWorkflowBindingAssignmentMapping;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Commands.CreateWorkflowBindingAssignmentMapping;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class UpdateWorkflowBindingAssignmentMappingCommandHandler
    : IRequestHandler<UpdateWorkflowBindingAssignmentMappingCommand, Result<WorkflowBindingAssignmentMappingDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowAssignmentGroupRepository _groupRepo;
    private readonly IWorkflowBindingAssignmentMappingRepository _mappingRepo;

    public UpdateWorkflowBindingAssignmentMappingCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkflowAssignmentGroupRepository groupRepo,
        IWorkflowBindingAssignmentMappingRepository mappingRepo)
    {
        _gate = gate;
        _groupRepo = groupRepo;
        _mappingRepo = mappingRepo;
    }

    public async Task<Result<WorkflowBindingAssignmentMappingDto>> Handle(
        UpdateWorkflowBindingAssignmentMappingCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowBindingAssignmentMappingDto>(gateResult.Error);

        var mapping = await _mappingRepo.GetByIdAsync(request.MappingId, cancellationToken);
        if (mapping is null)
            return Result.Failure<WorkflowBindingAssignmentMappingDto>(WorkflowErrors.AssignmentMapping.NotFound);

        if (mapping.OrganizationId != request.OrganizationId)
            return Result.Failure<WorkflowBindingAssignmentMappingDto>(WorkflowErrors.Forbidden);

        var group = await _groupRepo.GetByIdAsync(request.AssignmentGroupId, request.OrganizationId, cancellationToken);
        if (group is null)
            return Result.Failure<WorkflowBindingAssignmentMappingDto>(WorkflowErrors.AssignmentMapping.GroupNotInOrg);
        if (!group.IsActive)
            return Result.Failure<WorkflowBindingAssignmentMappingDto>(WorkflowErrors.AssignmentMapping.GroupInactive);

        mapping.Update(request.AssignmentGroupId, DateTime.UtcNow);
        await _mappingRepo.SaveChangesAsync(cancellationToken);

        return Result.Success(CreateWorkflowBindingAssignmentMappingCommandHandler.MapToDto(mapping, group.Name));
    }
}
