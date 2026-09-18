namespace Workflow.Application.Abstractions;

using Workflow.Application.DTOs;
using Workflow.Domain.Entities;

public interface IWorkflowHistoryBuilder
{
    Task<IReadOnlyList<WorkflowEventDto>> BuildAsync(
        WorkflowInstance instance,
        IReadOnlyList<WorkflowEvent> events,
        IReadOnlyList<ActivityInstance> activities,
        bool crossTenant,
        CancellationToken cancellationToken = default);
}
