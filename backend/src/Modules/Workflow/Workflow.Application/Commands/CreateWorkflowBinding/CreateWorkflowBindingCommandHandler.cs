namespace Workflow.Application.Commands.CreateWorkflowBinding;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

public sealed class CreateWorkflowBindingCommandHandler
    : IRequestHandler<CreateWorkflowBindingCommand, Result<WorkflowBindingDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowDefinitionRepository _definitionRepo;
    private readonly IWorkflowBindingRepository _bindingRepo;

    public CreateWorkflowBindingCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkflowDefinitionRepository definitionRepo,
        IWorkflowBindingRepository bindingRepo)
    {
        _gate = gate;
        _definitionRepo = definitionRepo;
        _bindingRepo = bindingRepo;
    }

    public async Task<Result<WorkflowBindingDto>> Handle(
        CreateWorkflowBindingCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowBindingDto>(gateResult.Error);

        var definition = await _definitionRepo.GetByIdAsync(request.DefinitionId, cancellationToken);
        if (definition is null)
            return Result.Failure<WorkflowBindingDto>(WorkflowErrors.Definition.NotFound);

        if (definition.OrganizationId != request.OrganizationId)
            return Result.Failure<WorkflowBindingDto>(WorkflowErrors.Binding.OrganizationMismatch);

        var exists = await _bindingRepo.BindingExistsAsync(
            request.DefinitionId, request.OrganizationId,
            request.ModuleKey, request.EntityType, request.TriggerEvent,
            cancellationToken);

        if (exists)
            return Result.Failure<WorkflowBindingDto>(WorkflowErrors.Binding.DuplicateBinding);

        var now = DateTime.UtcNow;
        var binding = WorkflowBinding.Create(
            request.DefinitionId, request.OrganizationId,
            request.ModuleKey, request.EntityType, request.TriggerEvent,
            now,
            description: request.Description,
            mode: request.Mode,
            versionPolicy: request.VersionPolicy,
            executionPolicy: request.ExecutionPolicy,
            fixedWorkflowVersionId: request.FixedWorkflowVersionId,
            startEventKey: request.StartEventKey,
            startConditionExpression: request.StartConditionExpression,
            screenKey: request.ScreenKey,
            inputMappingJson: request.InputMappingJson,
            outcomeMappingJson: request.OutcomeMappingJson,
            conditionJson: request.ConditionJson);

        await _bindingRepo.AddAsync(binding, cancellationToken);
        await _bindingRepo.SaveChangesAsync(cancellationToken);

        return Result.Success(MapToDto(binding, definition.Name));
    }

    internal static WorkflowBindingDto MapToDto(WorkflowBinding b, string? definitionName = null) =>
        new(b.Id, b.WorkflowDefinitionId, definitionName,
            b.OrganizationId, b.ModuleKey, b.EntityType, b.TriggerEvent,
            b.Description, b.Mode, b.VersionPolicy, b.ExecutionPolicy, b.FixedWorkflowVersionId,
            b.StartEventKey, b.StartConditionExpression,
            b.ScreenKey, b.InputMappingJson, b.OutcomeMappingJson, b.ConditionJson,
            b.IsActive, b.CreatedAt, b.UpdatedAt);
}
