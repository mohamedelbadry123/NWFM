namespace Workflow.Infrastructure.Services;

using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

internal sealed class WorkflowVersionResolver : IWorkflowVersionResolver
{
    private readonly IWorkflowVersionRepository _versionRepo;

    public WorkflowVersionResolver(IWorkflowVersionRepository versionRepo)
        => _versionRepo = versionRepo;

    public async Task<Result<WorkflowVersion>> ResolveAsync(
        WorkflowBinding binding, CancellationToken cancellationToken = default)
    {
        WorkflowVersion? version;

        if (binding.VersionPolicy == WorkflowVersionPolicy.Fixed && binding.FixedWorkflowVersionId.HasValue)
        {
            version = await _versionRepo.GetByIdWithProjectionAsync(
                binding.FixedWorkflowVersionId.Value, cancellationToken);

            if (version is null)
                return Result.Failure<WorkflowVersion>(WorkflowErrors.Version.NotFound);
        }
        else
        {
            version = await _versionRepo.GetLatestPublishedWithProjectionAsync(
                binding.WorkflowDefinitionId, cancellationToken);

            if (version is null)
                return Result.Failure<WorkflowVersion>(WorkflowErrors.Version.NoPublishedVersion);
        }

        if (version.Status != WorkflowVersionStatus.Published)
            return Result.Failure<WorkflowVersion>(WorkflowErrors.Version.NotPublished);

        return Result.Success(version);
    }
}
