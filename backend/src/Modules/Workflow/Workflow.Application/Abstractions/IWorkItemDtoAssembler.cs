namespace Workflow.Application.Abstractions;

using Workflow.Application.DTOs;
using Workflow.Domain.Entities;

public interface IWorkItemDtoAssembler
{
    Task<WorkItemDto> ToDtoAsync(WorkItem item, bool includeOutcomes, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkItemDto>> ToDtoListAsync(IReadOnlyList<WorkItem> items, CancellationToken cancellationToken = default);
}
