namespace Workflow.Application.Commands.ActivateWorkflowBinding;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

public sealed class ActivateWorkflowBindingCommandHandler
    : IRequestHandler<ActivateWorkflowBindingCommand, Result<bool>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowBindingRepository _repo;
    private readonly IWorkflowVersionRepository _versionRepo;
    private readonly IWorkflowAssignmentGroupRepository _groupRepo;
    private readonly IWorkflowDefinitionRepository _definitionRepo;

    public ActivateWorkflowBindingCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkflowBindingRepository repo,
        IWorkflowVersionRepository versionRepo,
        IWorkflowAssignmentGroupRepository groupRepo,
        IWorkflowDefinitionRepository definitionRepo)
    {
        _gate = gate;
        _repo = repo;
        _versionRepo = versionRepo;
        _groupRepo = groupRepo;
        _definitionRepo = definitionRepo;
    }

    public async Task<Result<bool>> Handle(
        ActivateWorkflowBindingCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<bool>(gateResult.Error);

        // IgnoreQueryFilters via GetByIdForSuperAdminAsync: this action is SuperAdmin-only
        // and the binding belongs to the selected organization, not the JWT tenant.
        var binding = await _repo.GetByIdForSuperAdminAsync(request.BindingId, cancellationToken);
        if (binding is null)
            return Result.Failure<bool>(WorkflowErrors.Binding.NotFound);

        var definition = await _definitionRepo.GetByIdAsync(binding.WorkflowDefinitionId, cancellationToken);
        if (definition is not null && definition.OrganizationId != binding.OrganizationId)
            return Result.Failure<bool>(WorkflowErrors.Binding.OrganizationMismatch);

        if (binding.Mode is WorkflowBindingMode.Shadow or WorkflowBindingMode.Active)
        {
            var version = binding.VersionPolicy == WorkflowVersionPolicy.Fixed && binding.FixedWorkflowVersionId.HasValue
                ? await _versionRepo.GetByIdWithProjectionAsync(binding.FixedWorkflowVersionId.Value, cancellationToken)
                : await _versionRepo.GetLatestPublishedWithProjectionAsync(binding.WorkflowDefinitionId, cancellationToken);

            if (version is null)
                return Result.Failure<bool>(WorkflowErrors.Version.NotFound);

            var userTasks = version.Activities.Where(a => a.ActivityType == ActivityType.UserTask);
            foreach (var task in userTasks)
            {
                var rule = task.AssignmentRules.FirstOrDefault(r => r.IsActive);
                var resolved = false;

                if (rule?.ReferenceId is { } groupId && groupId != Guid.Empty)
                {
                    var group = await _groupRepo.GetByIdAsync(groupId, binding.OrganizationId, cancellationToken);
                    resolved = group is not null && group.IsActive;
                }

                if (!resolved && !string.IsNullOrWhiteSpace(rule?.AssignmentKey))
                {
                    var byCode = await _groupRepo.GetByCodeAsync(
                        rule.AssignmentKey, binding.OrganizationId, cancellationToken);
                    resolved = byCode is not null;
                }

                if (!resolved)
                    return Result.Failure<bool>(WorkflowErrors.Binding.IncompleteMappings);
            }
        }

        binding.Activate(DateTime.UtcNow);
        await _repo.SaveChangesAsync(cancellationToken);

        return Result.Success(true);
    }
}
