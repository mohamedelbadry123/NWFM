namespace Workflow.Application.Queries.GetWorkflowBindingById;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Commands.CreateWorkflowBinding;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class GetWorkflowBindingByIdQueryHandler
    : IRequestHandler<GetWorkflowBindingByIdQuery, Result<WorkflowBindingDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowBindingRepository _repo;

    public GetWorkflowBindingByIdQueryHandler(
        IWorkflowFeatureGate gate, IWorkflowBindingRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<WorkflowBindingDto>> Handle(
        GetWorkflowBindingByIdQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowBindingDto>(gateResult.Error);

        // IgnoreQueryFilters via GetByIdForSuperAdminAsync: SuperAdmin catalog read.
        var binding = await _repo.GetByIdForSuperAdminAsync(request.BindingId, cancellationToken);
        if (binding is null)
            return Result.Failure<WorkflowBindingDto>(WorkflowErrors.Binding.NotFound);

        return Result.Success(CreateWorkflowBindingCommandHandler.MapToDto(binding));
    }
}
