namespace Workflow.Application.Queries.ListMyWorkflowNotifications;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class ListMyWorkflowNotificationsQueryHandler
    : IRequestHandler<ListMyWorkflowNotificationsQuery, Result<WorkflowNotificationLogPageDto>>
{
    private const int MaxVariablesJsonLength = 200;

    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowNotificationLogRepository _repo;

    public ListMyWorkflowNotificationsQueryHandler(
        IWorkflowFeatureGate gate,
        IWorkflowNotificationLogRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<WorkflowNotificationLogPageDto>> Handle(
        ListMyWorkflowNotificationsQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure)
            return Result.Failure<WorkflowNotificationLogPageDto>(gateResult.Error);

        var pageSize   = Math.Clamp(request.PageSize, 1, 50);
        var pageNumber = Math.Max(1, request.PageNumber);

        var (items, total) = await _repo.GetPagedByOrgAsync(
            request.OrganizationId,
            pageNumber,
            pageSize,
            request.RecipientUserId,
            cancellationToken);

        var dtos = items.Select(n => new WorkflowNotificationLogDto(
            n.Id,
            n.OrganizationId,
            n.TemplateKey,
            n.Channels,
            n.Status,
            n.CorrelationId,
            n.CreatedAt,
            n.VariablesJson.Length > MaxVariablesJsonLength
                ? n.VariablesJson[..MaxVariablesJsonLength] + "…"
                : n.VariablesJson)).ToList();

        return Result.Success(new WorkflowNotificationLogPageDto(dtos, total, pageNumber, pageSize));
    }
}
