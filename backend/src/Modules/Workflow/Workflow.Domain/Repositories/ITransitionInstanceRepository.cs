namespace Workflow.Domain.Repositories;

using Workflow.Domain.Entities;

public interface ITransitionInstanceRepository
{
    Task AddAsync(TransitionInstance transitionInstance, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TransitionInstance>> GetByInstanceIdAsync(Guid workflowInstanceId, CancellationToken cancellationToken = default);
}
