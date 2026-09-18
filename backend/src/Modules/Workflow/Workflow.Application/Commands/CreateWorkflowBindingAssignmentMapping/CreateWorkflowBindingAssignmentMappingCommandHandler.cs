namespace Workflow.Application.Commands.CreateWorkflowBindingAssignmentMapping;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

public sealed class CreateWorkflowBindingAssignmentMappingCommandHandler
    : IRequestHandler<CreateWorkflowBindingAssignmentMappingCommand, Result<WorkflowBindingAssignmentMappingDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowBindingRepository _bindingRepo;
    private readonly IWorkflowAssignmentGroupRepository _groupRepo;
    private readonly IWorkflowBindingAssignmentMappingRepository _mappingRepo;

    public CreateWorkflowBindingAssignmentMappingCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkflowBindingRepository bindingRepo,
        IWorkflowAssignmentGroupRepository groupRepo,
        IWorkflowBindingAssignmentMappingRepository mappingRepo)
    {
        _gate = gate;
        _bindingRepo = bindingRepo;
        _groupRepo = groupRepo;
        _mappingRepo = mappingRepo;
    }

    public async Task<Result<WorkflowBindingAssignmentMappingDto>> Handle(
        CreateWorkflowBindingAssignmentMappingCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowBindingAssignmentMappingDto>(gateResult.Error);

        // IgnoreQueryFilters via GetByIdForSuperAdminAsync: SuperAdmin mapping create.
        var binding = await _bindingRepo.GetByIdForSuperAdminAsync(request.BindingId, cancellationToken);
        if (binding is null)
            return Result.Failure<WorkflowBindingAssignmentMappingDto>(WorkflowErrors.Binding.NotFound);

        if (binding.OrganizationId != request.OrganizationId)
            return Result.Failure<WorkflowBindingAssignmentMappingDto>(WorkflowErrors.Forbidden);

        // Validate the AssignmentGroup belongs to the org and is active
        var group = await _groupRepo.GetByIdAsync(request.AssignmentGroupId, request.OrganizationId, cancellationToken);
        if (group is null)
            return Result.Failure<WorkflowBindingAssignmentMappingDto>(WorkflowErrors.AssignmentMapping.GroupNotInOrg);
        if (!group.IsActive)
            return Result.Failure<WorkflowBindingAssignmentMappingDto>(WorkflowErrors.AssignmentMapping.GroupInactive);

        // Validate no duplicate mapping for this AssignmentKey on this binding
        var exists = await _mappingRepo.MappingExistsAsync(
            request.BindingId, request.AssignmentKey, request.OrganizationId, cancellationToken);
        if (exists)
            return Result.Failure<WorkflowBindingAssignmentMappingDto>(WorkflowErrors.AssignmentMapping.DuplicateKey);

        var now = DateTime.UtcNow;
        var mapping = WorkflowBindingAssignmentMapping.Create(
            request.OrganizationId, request.BindingId, request.AssignmentKey,
            request.AssignmentGroupId, now);

        await _mappingRepo.AddAsync(mapping, cancellationToken);
        await _mappingRepo.SaveChangesAsync(cancellationToken);

        return Result.Success(MapToDto(mapping, group.Name));
    }

    internal static WorkflowBindingAssignmentMappingDto MapToDto(
        WorkflowBindingAssignmentMapping m, string? groupName = null) =>
        new(m.Id, m.OrganizationId, m.WorkflowBindingId, m.AssignmentKey,
            m.AssignmentGroupId, groupName, m.IsActive, m.CreatedAt, m.UpdatedAt);
}
