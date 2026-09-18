namespace Workflow.Application.Queries.ListWorkflowRequests;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class ListWorkflowRequestsQueryHandler
    : IRequestHandler<ListWorkflowRequestsQuery, Result<WorkflowRequestPageDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowRequestRepository _repo;
    private readonly IWorkflowRequestProjector _projector;
    private readonly IWorkflowAssignmentGroupRepository _groups;

    public ListWorkflowRequestsQueryHandler(
        IWorkflowFeatureGate gate,
        IWorkflowRequestRepository repo,
        IWorkflowRequestProjector projector,
        IWorkflowAssignmentGroupRepository groups)
    {
        _gate = gate;
        _repo = repo;
        _projector = projector;
        _groups = groups;
    }

    public async Task<Result<WorkflowRequestPageDto>> Handle(
        ListWorkflowRequestsQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowRequestPageDto>(gateResult.Error);

        await _projector.BackfillMissingForOrgAsync(request.OrganizationId, cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var pageNumber = Math.Max(1, request.PageNumber);

        var (items, total) = await _repo.GetPagedAsync(
            request.OrganizationId, pageNumber, pageSize, request.Search, request.Status,
            request.Service, request.CurrentStep, request.OriginalGroupId, request.FromUtc, request.ToUtc,
            request.SlaStatus, request.SortBy, DateTime.UtcNow, cancellationToken);

        var kpis = await _repo.GetKpisAsync(request.OrganizationId, DateTime.UtcNow, cancellationToken);

        var groupIds = items
            .SelectMany(r => new Guid?[] { r.OriginalAssignedGroupId, r.CurrentAssignedGroupId })
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var names = await _groups.GetNamesByIdsAsync(request.OrganizationId, groupIds, cancellationToken);

        var dtos = items.Select(r => Map(r, names)).ToList();

        return Result.Success(new WorkflowRequestPageDto(
            dtos, total, pageNumber, pageSize,
            new WorkflowRequestKpiDto(kpis.Total, kpis.InProgress, kpis.Completed, kpis.Breached)));
    }

    internal static WorkflowRequestDto Map(
        Domain.Entities.WorkflowRequest r, IReadOnlyDictionary<Guid, string> names)
    {
        names.TryGetValue(r.OriginalAssignedGroupId ?? Guid.Empty, out var originalName);
        names.TryGetValue(r.CurrentAssignedGroupId ?? Guid.Empty, out var currentName);

        int? remaining = null;
        if (r.CurrentTaskDueAtUtc is DateTime due
            && r.Status == Domain.Enums.WorkflowInstanceStatus.Running)
        {
            remaining = (int)Math.Round((due - DateTime.UtcNow).TotalMinutes);
        }

        return new WorkflowRequestDto(
            r.Id, r.OrganizationId, r.RequestNumber, r.WorkflowBindingId, r.WorkflowInstanceId,
            r.BusinessEntityType, r.BusinessEntityId, r.ServiceKey, r.ServiceNameEn, r.ServiceNameAr,
            r.ScreenKey, r.TriggerEventKey, r.RequestDate, r.RequesterUserId, r.Status,
            r.CurrentActivityInstanceId, r.CurrentActivityNameEn, r.CurrentActivityNameAr,
            r.OriginalAssignedGroupId, originalName, r.CurrentAssignedGroupId, currentName,
            r.CurrentTaskSlaMinutes, r.CurrentTaskDueAtUtc, remaining, r.CompletedAtUtc,
            r.CorrelationId, r.CurrentClaimedByUserId, r.CreatedAt, r.UpdatedAt);
    }
}
