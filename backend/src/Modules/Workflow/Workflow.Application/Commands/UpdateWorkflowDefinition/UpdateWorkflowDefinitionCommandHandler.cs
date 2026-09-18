namespace Workflow.Application.Commands.UpdateWorkflowDefinition;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Commands.CreateWorkflowDefinition;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class UpdateWorkflowDefinitionCommandHandler
    : IRequestHandler<UpdateWorkflowDefinitionCommand, Result<WorkflowDefinitionDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowDefinitionRepository _repo;

    public UpdateWorkflowDefinitionCommandHandler(
        IWorkflowFeatureGate gate, IWorkflowDefinitionRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<WorkflowDefinitionDto>> Handle(
        UpdateWorkflowDefinitionCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowDefinitionDto>(gateResult.Error);

        var definition = await _repo.GetByIdAsync(request.DefinitionId, cancellationToken);
        if (definition is null)
            return Result.Failure<WorkflowDefinitionDto>(WorkflowErrors.Definition.NotFound);

        definition.Update(request.Name, request.NameAr, request.Description, request.DescriptionAr, DateTime.UtcNow);
        await _repo.SaveChangesAsync(cancellationToken);

        var versionCount = await _repo.GetVersionCountAsync(definition.Id, cancellationToken);
        return Result.Success(CreateWorkflowDefinitionCommandHandler.MapToDto(definition, versionCount));
    }
}
