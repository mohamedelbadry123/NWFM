namespace Workflow.Application.Queries.GetWorkloadDashboard;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class GetWorkloadDashboardQueryHandler
    : IRequestHandler<GetWorkloadDashboardQuery, Result<WorkloadDashboardDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkItemRepository _workItems;

    public GetWorkloadDashboardQueryHandler(IWorkflowFeatureGate gate, IWorkItemRepository workItems)
    {
        _gate = gate;
        _workItems = workItems;
    }

    public async Task<Result<WorkloadDashboardDto>> Handle(
        GetWorkloadDashboardQuery request, CancellationToken cancellationToken)
    {
        var gate = _gate.EnsureEnabled();
        if (gate.IsFailure)
            return Result.Failure<WorkloadDashboardDto>(gate.Error);

        var now = DateTime.UtcNow;
        var todayStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);

        var groups = await _workItems.GetWorkloadByGroupAsync(
            request.OrganizationId, now, cancellationToken);
        var totals = await _workItems.GetWorkloadTotalsAsync(
            request.OrganizationId, now, todayStart, cancellationToken);

        var groupDtos = groups
            .Select(g => new WorkloadGroupDto(g.GroupId, g.GroupName, g.PendingCount, g.OverdueCount, g.ClaimedCount))
            .ToList();

        return Result.Success(new WorkloadDashboardDto(
            groupDtos,
            new WorkloadTotalsDto(totals.Open, totals.Overdue, totals.CompletedToday)));
    }
}
