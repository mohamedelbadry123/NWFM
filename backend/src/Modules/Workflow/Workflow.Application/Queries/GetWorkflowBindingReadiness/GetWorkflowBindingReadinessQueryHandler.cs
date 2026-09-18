namespace Workflow.Application.Queries.GetWorkflowBindingReadiness;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

public sealed class GetWorkflowBindingReadinessQueryHandler
    : IRequestHandler<GetWorkflowBindingReadinessQuery, Result<WorkflowBindingReadinessDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowBindingRepository _bindings;
    private readonly IWorkflowVersionRepository _versions;
    private readonly IWorkflowAssignmentGroupRepository _groups;

    public GetWorkflowBindingReadinessQueryHandler(
        IWorkflowFeatureGate gate,
        IWorkflowBindingRepository bindings,
        IWorkflowVersionRepository versions,
        IWorkflowAssignmentGroupRepository groups)
    {
        _gate = gate;
        _bindings = bindings;
        _versions = versions;
        _groups = groups;
    }

    public async Task<Result<WorkflowBindingReadinessDto>> Handle(
        GetWorkflowBindingReadinessQuery request,
        CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowBindingReadinessDto>(gateResult.Error);

        // IgnoreQueryFilters via GetByIdForSuperAdminAsync: SuperAdmin readiness check.
        var binding = await _bindings.GetByIdForSuperAdminAsync(request.BindingId, cancellationToken);
        if (binding is null)
            return Result.Failure<WorkflowBindingReadinessDto>(WorkflowErrors.Binding.NotFound);

        var version = binding.VersionPolicy == WorkflowVersionPolicy.Fixed
            && binding.FixedWorkflowVersionId.HasValue
            ? await _versions.GetByIdWithProjectionAsync(binding.FixedWorkflowVersionId.Value, cancellationToken)
            : await _versions.GetLatestPublishedWithProjectionAsync(binding.WorkflowDefinitionId, cancellationToken);

        if (version is null)
        {
            return Result.Success(new WorkflowBindingReadinessDto(
                binding.Id,
                binding.OrganizationId,
                null,
                IsReady: false,
                RequiredAssignmentKeys: [],
                MappedAssignmentKeys: [],
                UnmappedAssignmentKeys: [],
                BlockingReason: "No published workflow version is available for this binding."));
        }

        var required = new List<string>();
        var resolved = new List<string>();
        var missing = new List<string>();

        foreach (var task in version.Activities.Where(a => a.ActivityType == ActivityType.UserTask))
        {
            var rule = task.AssignmentRules.FirstOrDefault(r => r.IsActive);
            var label = rule?.AssignmentKey ?? task.Name ?? task.NodeKey;
            required.Add(label);

            var ok = false;
            if (rule?.ReferenceId is { } groupId && groupId != Guid.Empty)
            {
                var group = await _groups.GetByIdAsync(groupId, binding.OrganizationId, cancellationToken);
                ok = group is not null && group.IsActive;
            }

            if (!ok && !string.IsNullOrWhiteSpace(rule?.AssignmentKey))
            {
                var byCode = await _groups.GetByCodeAsync(rule.AssignmentKey, binding.OrganizationId, cancellationToken);
                ok = byCode is not null;
            }

            if (ok) resolved.Add(label);
            else missing.Add(label);
        }

        var ready = missing.Count == 0;
        return Result.Success(new WorkflowBindingReadinessDto(
            binding.Id,
            binding.OrganizationId,
            version.Id,
            IsReady: ready,
            RequiredAssignmentKeys: required,
            MappedAssignmentKeys: resolved,
            UnmappedAssignmentKeys: missing,
            BlockingReason: ready
                ? null
                : $"User tasks without an organization group: {string.Join(", ", missing)}"));
    }
}
