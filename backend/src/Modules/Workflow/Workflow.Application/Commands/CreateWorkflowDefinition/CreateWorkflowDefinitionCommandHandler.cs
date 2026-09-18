namespace Workflow.Application.Commands.CreateWorkflowDefinition;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

public sealed class CreateWorkflowDefinitionCommandHandler
    : IRequestHandler<CreateWorkflowDefinitionCommand, Result<WorkflowDefinitionDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowDefinitionRepository _repo;

    public CreateWorkflowDefinitionCommandHandler(
        IWorkflowFeatureGate gate, IWorkflowDefinitionRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<WorkflowDefinitionDto>> Handle(
        CreateWorkflowDefinitionCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowDefinitionDto>(gateResult.Error);

        var keyExists = await _repo.KeyExistsAsync(
            request.OrganizationId, request.DefinitionKey, cancellationToken);
        if (keyExists)
            return Result.Failure<WorkflowDefinitionDto>(WorkflowErrors.Definition.DuplicateKey);

        var now = DateTime.UtcNow;
        var definition = WorkflowDefinition.Create(
            request.OrganizationId,
            request.DefinitionKey, request.Name, now,
            request.NameAr, request.Description, request.DescriptionAr);

        await _repo.AddAsync(definition, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        return Result.Success(MapToDto(definition, 0));
    }

    internal static WorkflowDefinitionDto MapToDto(WorkflowDefinition d, int versionCount) =>
        new(d.Id, d.OrganizationId, d.DefinitionKey, d.Name, d.NameAr,
            d.Description, d.DescriptionAr, d.IsActive, versionCount, d.CreatedAt, d.UpdatedAt);
}
