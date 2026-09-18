namespace Workflow.Application.Commands.CompleteWorkItem;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Application.Helpers;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

public sealed class CompleteWorkItemCommandHandler
    : IRequestHandler<CompleteWorkItemCommand, Result<WorkItemDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkItemRepository _workItemRepo;
    private readonly IWorkflowRuntimeEngine _engine;
    private readonly IWorkflowEventAppender _events;
    private readonly IWorkItemDtoAssembler _assembler;
    private readonly IWorkflowInstanceRepository _instances;
    private readonly IWorkflowVersionRepository _versions;
    private readonly IActivityInstanceRepository _activities;
    private readonly IWorkflowAssignmentGroupRepository _groupRepo;
    private readonly IWorkflowDepartmentRepository _departmentRepo;
    private readonly IWorkflowCandidateFactory _candidateFactory;
    private readonly IWorkItemCandidateRepository _candidateRepo;

    public CompleteWorkItemCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkItemRepository workItemRepo,
        IWorkflowRuntimeEngine engine,
        IWorkflowEventAppender events,
        IWorkItemDtoAssembler assembler,
        IWorkflowInstanceRepository instances,
        IWorkflowVersionRepository versions,
        IActivityInstanceRepository activities,
        IWorkflowAssignmentGroupRepository groupRepo,
        IWorkflowDepartmentRepository departmentRepo,
        IWorkflowCandidateFactory candidateFactory,
        IWorkItemCandidateRepository candidateRepo)
    {
        _gate             = gate;
        _workItemRepo     = workItemRepo;
        _engine           = engine;
        _events           = events;
        _assembler        = assembler;
        _instances        = instances;
        _versions         = versions;
        _activities       = activities;
        _groupRepo        = groupRepo;
        _departmentRepo   = departmentRepo;
        _candidateFactory = candidateFactory;
        _candidateRepo    = candidateRepo;
    }

    public async Task<Result<WorkItemDto>> Handle(
        CompleteWorkItemCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkItemDto>(gateResult.Error);

        var item = await _workItemRepo.GetByIdAsync(request.WorkItemId, cancellationToken);
        if (item is null || item.OrganizationId != request.OrganizationId)
            return Result.Failure<WorkItemDto>(WorkflowErrors.WorkItem.NotFound);

        if (item.Status == WorkItemStatus.Completed)
            return Result.Failure<WorkItemDto>(WorkflowErrors.WorkItem.AlreadyCompleted);

        if (item.Status != WorkItemStatus.Claimed)
            return Result.Failure<WorkItemDto>(WorkflowErrors.WorkItem.NotClaimed);

        if (item.ClaimedByUserId != request.UserId)
            return Result.Failure<WorkItemDto>(WorkflowErrors.WorkItem.NotClaimedByUser);

        var (outcomeError, outcome) = await LoadOutcomeAsync(item, request.ActionTaken, request.Comment, cancellationToken);
        if (outcomeError is not null)
            return Result.Failure<WorkItemDto>(outcomeError);

        var isRedirect = WorkflowOutcomeKeys.IsRedirect(request.ActionTaken, outcome?.ResultValue);
        if (isRedirect)
            return await RedirectAsync(item, request, cancellationToken);

        var now = DateTime.UtcNow;
        item.Complete(request.UserId, request.ActionTaken, now, request.Comment);

        var activity = await _activities.GetByIdAsync(item.ActivityInstanceId, cancellationToken);
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            workItemId = item.Id,
            outcome = request.ActionTaken,
            comment = request.Comment
        });

        await _events.AppendAsync(
            request.OrganizationId, item.WorkflowInstanceId,
            WorkflowEventType.WorkItemCompleted, now,
            activityNodeKey: activity?.ActivityNodeKey,
            actorUserId: request.UserId,
            payloadJson: payload,
            cancellationToken: cancellationToken);

        var advanceResult = await _engine.AdvanceAsync(
            item.WorkflowInstanceId, request.WorkItemId, now, cancellationToken);

        if (advanceResult.IsFailure) return Result.Failure<WorkItemDto>(advanceResult.Error);

        return Result.Success(await _assembler.ToDtoAsync(item, includeOutcomes: false, cancellationToken));
    }

    private async Task<Result<WorkItemDto>> RedirectAsync(
        WorkItem item,
        CompleteWorkItemCommand request,
        CancellationToken cancellationToken)
    {
        var targetGroupId = request.RedirectAssignmentGroupId;
        if (!targetGroupId.HasValue && request.RedirectDepartmentId.HasValue)
        {
            var dept = await _departmentRepo.GetByIdAsync(
                request.RedirectDepartmentId.Value, request.OrganizationId, cancellationToken);
            if (dept is null || !dept.IsActive)
                return Result.Failure<WorkItemDto>(WorkflowErrors.WorkItem.RedirectTargetRequired);
            if (!dept.DefaultAssignmentGroupId.HasValue)
                return Result.Failure<WorkItemDto>(WorkflowErrors.WorkItem.DepartmentHasNoDefaultGroup);
            targetGroupId = dept.DefaultAssignmentGroupId;
        }

        if (!targetGroupId.HasValue)
            return Result.Failure<WorkItemDto>(WorkflowErrors.WorkItem.RedirectTargetRequired);

        var group = await _groupRepo.GetByIdAsync(targetGroupId.Value, request.OrganizationId, cancellationToken);
        if (group is null || !group.IsActive)
            return Result.Failure<WorkItemDto>(WorkflowErrors.AssignmentGroup.NotFound);

        var now = DateTime.UtcNow;
        item.Reassign(targetGroupId.Value, now);
        await _workItemRepo.SaveChangesAsync(cancellationToken);

        var candidates = await _candidateFactory.CreateCandidatesAsync(
            request.OrganizationId, item.Id, targetGroupId.Value, now, cancellationToken);
        await _candidateRepo.AddRangeAsync(candidates, cancellationToken);

        await _events.AppendAsync(
            request.OrganizationId, item.WorkflowInstanceId,
            WorkflowEventType.WorkItemReassigned, now,
            actorUserId: request.UserId,
            payloadJson: $"{{\"workItemId\":\"{item.Id}\",\"outcome\":\"{WorkflowOutcomeKeys.Redirect}\",\"newGroupId\":\"{targetGroupId.Value}\"}}",
            cancellationToken: cancellationToken);

        return Result.Success(await _assembler.ToDtoAsync(item, includeOutcomes: true, cancellationToken));
    }

    private async Task<(Error? Error, ActivityOutcomeDefinition? Outcome)> LoadOutcomeAsync(
        WorkItem item, string actionTaken, string? comment, CancellationToken cancellationToken)
    {
        var instance = await _instances.GetByIdAsync(item.WorkflowInstanceId, cancellationToken);
        if (instance is null)
            return (null, null);

        var activity = await _activities.GetByIdAsync(item.ActivityInstanceId, cancellationToken);
        var version = await _versions.GetByIdWithProjectionAsync(instance.PinnedWorkflowVersionId, cancellationToken);
        if (version is null)
            return (null, null);

        var def = version.Activities.FirstOrDefault(a =>
            a.NodeKey == (activity?.ActivityNodeKey ?? instance.CurrentActivityNodeKey));
        var outcomes = def?.Outcomes.Where(o => o.IsActive).ToList() ?? [];
        if (outcomes.Count == 0)
            return (null, null);

        var match = outcomes.FirstOrDefault(o =>
            string.Equals(o.OutcomeKey, actionTaken, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return (WorkflowErrors.WorkItem.InvalidOutcome, null);

        if (match.RequiresComment && string.IsNullOrWhiteSpace(comment))
            return (WorkflowErrors.WorkItem.CommentRequired, match);

        return (null, match);
    }
}
