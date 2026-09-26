namespace Workflow.Infrastructure.Services;

using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Domain.Repositories;

internal sealed class WorkflowAssignmentResolver : IWorkflowAssignmentResolver
{
    private readonly IWorkflowBindingAssignmentMappingRepository _mappingRepo;
    private readonly IWorkflowAssignmentGroupRepository _groupRepo;

    public WorkflowAssignmentResolver(
        IWorkflowBindingAssignmentMappingRepository mappingRepo,
        IWorkflowAssignmentGroupRepository groupRepo)
    {
        _mappingRepo = mappingRepo;
        _groupRepo = groupRepo;
    }

    public Task<Result<Guid>> ResolveGroupAsync(
        Guid organizationId,
        Guid workflowBindingId,
        string assignmentKey,
        CancellationToken cancellationToken = default)
        => ResolveGroupAsync(organizationId, workflowBindingId, assignmentKey, null, cancellationToken);

    public async Task<Result<Guid>> ResolveGroupAsync(
        Guid organizationId,
        Guid workflowBindingId,
        string? assignmentKey,
        Guid? assignmentGroupId,
        CancellationToken cancellationToken = default)
    {
        if (assignmentGroupId is { } groupId && groupId != Guid.Empty)
        {
            var byId = await _groupRepo.GetByIdAsync(groupId, organizationId, cancellationToken);
            if (byId is not null && byId.IsActive)
                return Result.Success(byId.Id);

            return Result.Failure<Guid>(WorkflowErrors.AssignmentGroup.NotFound);
        }

        if (!string.IsNullOrWhiteSpace(assignmentKey))
        {
            var byCode = await _groupRepo.GetByCodeAsync(assignmentKey.Trim(), organizationId, cancellationToken);
            if (byCode is not null && byCode.IsActive)
                return Result.Success(byCode.Id);

            var mapping = await _mappingRepo.GetByAssignmentKeyAsync(
                workflowBindingId, organizationId, assignmentKey, cancellationToken);
            if (mapping is not null)
            {
                var mappedGroup = await _groupRepo.GetByIdAsync(mapping.AssignmentGroupId, organizationId, cancellationToken);
                if (mappedGroup is not null && mappedGroup.IsActive) return Result.Success(mappedGroup.Id);
            }
        }

        return Result.Failure<Guid>(WorkflowErrors.Instance.AssignmentKeyNotMapped);
    }
}
