namespace Workflow.Application.Queries.ListWorkflowActionsCatalog;

using MediatR;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.DTOs;

public sealed class ListWorkflowActionsCatalogQueryHandler
    : IRequestHandler<ListWorkflowActionsCatalogQuery, Result<IReadOnlyList<WorkflowActionCatalogEntryDto>>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowActionRegistry _registry;

    public ListWorkflowActionsCatalogQueryHandler(
        IWorkflowFeatureGate gate, IWorkflowActionRegistry registry)
    {
        _gate = gate;
        _registry = registry;
    }

    public Task<Result<IReadOnlyList<WorkflowActionCatalogEntryDto>>> Handle(
        ListWorkflowActionsCatalogQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure)
            return Task.FromResult(Result.Failure<IReadOnlyList<WorkflowActionCatalogEntryDto>>(gateResult.Error));

        IReadOnlyList<WorkflowActionCatalogEntryDto> entries = _registry.GetAll()
            .Select(d => new WorkflowActionCatalogEntryDto(d.ActionKey, d.NameEn, d.NameAr, d.ModuleKey))
            .ToList();

        return Task.FromResult(Result.Success(entries));
    }
}
