namespace Workflow.Domain.Repositories;

using Workflow.Domain.Entities;

public interface IActivityInstanceRepository
{
    Task<ActivityInstance?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityInstance>> GetByInstanceIdAsync(Guid workflowInstanceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityInstance>> GetByInstanceIdForSuperAdminAsync(Guid workflowInstanceId, CancellationToken cancellationToken = default);
    Task AddAsync(ActivityInstance activityInstance, CancellationToken cancellationToken = default);
}
