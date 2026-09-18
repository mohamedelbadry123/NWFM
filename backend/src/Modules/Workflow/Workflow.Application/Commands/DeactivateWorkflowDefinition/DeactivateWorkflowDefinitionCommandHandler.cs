namespace Workflow.Application.Commands.DeactivateWorkflowDefinition;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Commands.CreateWorkflowDefinition;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class DeactivateWorkflowDefinitionCommandHandler
    : IRequestHandler<DeactivateWorkflowDefinitionCommand, Result<WorkflowDefinitionDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowDefinitionRepository _repo;

    public DeactivateWorkflowDefinitionCommandHandler(
        IWorkflowFeatureGate gate, IWorkflowDefinitionRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<WorkflowDefinitionDto>> Handle(
        DeactivateWorkflowDefinitionCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowDefinitionDto>(gateResult.Error);

        var definition = await _repo.GetByIdAsync(request.DefinitionId, cancellationToken);
        if (definition is null)
            return Result.Failure<WorkflowDefinitionDto>(WorkflowErrors.Definition.NotFound);

        definition.Deactivate(DateTime.UtcNow);
        await _repo.SaveChangesAsync(cancellationToken);

        var versionCount = await _repo.GetVersionCountAsync(definition.Id, cancellationToken);
        return Result.Success(CreateWorkflowDefinitionCommandHandler.MapToDto(definition, versionCount));
    }
}
