namespace Workflow.Application.Queries.GetWorkflowBindingForSuperAdmin;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Commands.CreateWorkflowBinding;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class GetWorkflowBindingForSuperAdminQueryHandler
    : IRequestHandler<GetWorkflowBindingForSuperAdminQuery, Result<WorkflowBindingDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowBindingRepository _repo;

    public GetWorkflowBindingForSuperAdminQueryHandler(
        IWorkflowFeatureGate gate, IWorkflowBindingRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<WorkflowBindingDto>> Handle(
        GetWorkflowBindingForSuperAdminQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowBindingDto>(gateResult.Error);

        var binding = await _repo.GetByIdForSuperAdminAsync(request.BindingId, cancellationToken);
        if (binding is null)
            return Result.Failure<WorkflowBindingDto>(WorkflowErrors.Binding.NotFound);

        return Result.Success(CreateWorkflowBindingCommandHandler.MapToDto(binding));
    }
}
