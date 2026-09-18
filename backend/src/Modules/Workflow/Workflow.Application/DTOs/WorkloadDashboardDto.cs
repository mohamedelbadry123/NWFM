namespace Workflow.Application.DTOs;

public sealed record WorkloadDashboardDto(
    IReadOnlyList<WorkloadGroupDto> Groups,
    WorkloadTotalsDto Totals);

public sealed record WorkloadGroupDto(
    Guid GroupId,
    string Name,
    int PendingCount,
    int OverdueCount,
    int ClaimedCount);

public sealed record WorkloadTotalsDto(
    int Open,
    int Overdue,
    int CompletedToday);
