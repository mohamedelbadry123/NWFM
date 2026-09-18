namespace Workflow.Application.Commands.UpdateWorkflowBinding;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Commands.CreateWorkflowBinding;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class UpdateWorkflowBindingCommandHandler
    : IRequestHandler<UpdateWorkflowBindingCommand, Result<WorkflowBindingDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowBindingRepository _repo;

    public UpdateWorkflowBindingCommandHandler(
        IWorkflowFeatureGate gate, IWorkflowBindingRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<WorkflowBindingDto>> Handle(
        UpdateWorkflowBindingCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowBindingDto>(gateResult.Error);

        // IgnoreQueryFilters via GetByIdForSuperAdminAsync: SuperAdmin catalog update.
        var binding = await _repo.GetByIdForSuperAdminAsync(request.BindingId, cancellationToken);
        if (binding is null)
            return Result.Failure<WorkflowBindingDto>(WorkflowErrors.Binding.NotFound);

        binding.Update(
            request.ModuleKey, request.EntityType, request.TriggerEvent,
            request.Description, request.Mode, request.VersionPolicy,
            request.FixedWorkflowVersionId, request.StartEventKey,
            request.StartConditionExpression, DateTime.UtcNow,
            request.ScreenKey, request.InputMappingJson,
            request.OutcomeMappingJson, request.ConditionJson,
            request.ExecutionPolicy);

        await _repo.SaveChangesAsync(cancellationToken);

        return Result.Success(CreateWorkflowBindingCommandHandler.MapToDto(binding));
    }
}
