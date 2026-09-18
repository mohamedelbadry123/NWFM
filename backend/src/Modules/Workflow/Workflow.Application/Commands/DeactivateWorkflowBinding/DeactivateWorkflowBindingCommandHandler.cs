namespace Workflow.Application.Commands.DeactivateWorkflowBinding;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Domain.Repositories;

public sealed class DeactivateWorkflowBindingCommandHandler
    : IRequestHandler<DeactivateWorkflowBindingCommand, Result<bool>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowBindingRepository _repo;

    public DeactivateWorkflowBindingCommandHandler(
        IWorkflowFeatureGate gate, IWorkflowBindingRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<bool>> Handle(
        DeactivateWorkflowBindingCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<bool>(gateResult.Error);

        // IgnoreQueryFilters via GetByIdForSuperAdminAsync: SuperAdmin-only deactivate.
        var binding = await _repo.GetByIdForSuperAdminAsync(request.BindingId, cancellationToken);
        if (binding is null)
            return Result.Failure<bool>(WorkflowErrors.Binding.NotFound);

        binding.Deactivate(DateTime.UtcNow);
        await _repo.SaveChangesAsync(cancellationToken);

        return Result.Success(true);
    }
}
