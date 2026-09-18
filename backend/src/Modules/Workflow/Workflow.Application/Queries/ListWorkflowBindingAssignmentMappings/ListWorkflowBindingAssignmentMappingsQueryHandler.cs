namespace Workflow.Application.Queries.ListWorkflowBindingAssignmentMappings;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Commands.CreateWorkflowBindingAssignmentMapping;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class ListWorkflowBindingAssignmentMappingsQueryHandler
    : IRequestHandler<ListWorkflowBindingAssignmentMappingsQuery, Result<List<WorkflowBindingAssignmentMappingDto>>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowBindingRepository _bindingRepo;
    private readonly IWorkflowBindingAssignmentMappingRepository _mappingRepo;

    public ListWorkflowBindingAssignmentMappingsQueryHandler(
        IWorkflowFeatureGate gate,
        IWorkflowBindingRepository bindingRepo,
        IWorkflowBindingAssignmentMappingRepository mappingRepo)
    {
        _gate = gate;
        _bindingRepo = bindingRepo;
        _mappingRepo = mappingRepo;
    }

    public async Task<Result<List<WorkflowBindingAssignmentMappingDto>>> Handle(
        ListWorkflowBindingAssignmentMappingsQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<List<WorkflowBindingAssignmentMappingDto>>(gateResult.Error);

        // IgnoreQueryFilters via GetByIdForSuperAdminAsync: SuperAdmin mapping list.
        var binding = await _bindingRepo.GetByIdForSuperAdminAsync(request.BindingId, cancellationToken);
        if (binding is null)
            return Result.Failure<List<WorkflowBindingAssignmentMappingDto>>(WorkflowErrors.Binding.NotFound);

        if (binding.OrganizationId != request.OrganizationId)
            return Result.Failure<List<WorkflowBindingAssignmentMappingDto>>(WorkflowErrors.Forbidden);

        var mappings = await _mappingRepo.GetByBindingIdAsync(
            request.BindingId, request.OrganizationId, cancellationToken);

        var dtos = mappings.Select(m => CreateWorkflowBindingAssignmentMappingCommandHandler.MapToDto(m)).ToList();
        return Result.Success(dtos);
    }
}
