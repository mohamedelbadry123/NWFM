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
using NWFM.Shared.Integration.Forms;
using Workflow.Application.Workspace;
using Workflow.Application.Integrations;

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
    private readonly IFormGateway? _forms;

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
        IWorkItemCandidateRepository candidateRepo, IFormGateway? forms = null)
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
        _forms = forms;
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
        {
            // Completed visits are immutable receipts, including when a response was lost.
            if (request.FormVisitId is Guid replayId && item.CompletedByUserId == request.UserId && item.FormProgressJson is not null
                && System.Text.Json.JsonSerializer.Deserialize<ActivityFormProgress>(item.FormProgressJson)?.Visits.Any(v => v.VisitId == replayId && v.ActorId == request.UserId) == true)
                return Result.Success(await _assembler.ToDtoAsync(item, true, cancellationToken));
            return Result.Failure<WorkItemDto>(WorkflowErrors.WorkItem.AlreadyCompleted);
        }

        if (item.Status != WorkItemStatus.Claimed)
            return Result.Failure<WorkItemDto>(WorkflowErrors.WorkItem.NotClaimed);

        if (item.ClaimedByUserId != request.UserId)
            return Result.Failure<WorkItemDto>(WorkflowErrors.WorkItem.NotClaimedByUser);

        var workspaceInstance = await _instances.GetByIdAsync(item.WorkflowInstanceId, cancellationToken);
        var workspaceVersion = workspaceInstance is null ? null : await _versions.GetByIdWithProjectionAsync(workspaceInstance.PinnedWorkflowVersionId, cancellationToken);
        if (workspaceVersion?.WorkspaceJson is not null)
        {
            if (!await _groupRepo.IsUserMemberOfGroupAsync(item.AssignmentGroupId, request.UserId, request.OrganizationId, cancellationToken))
                return Result.Failure<WorkItemDto>(new Error("Workflow.Assignment.Membership", "The user no longer belongs to the assigned group."));
            var execution = await _activities.GetByIdAsync(item.ActivityInstanceId, cancellationToken);
            if (execution?.ActivityType == ActivityType.MainActivity && execution.Phase != "AwaitingApproval")
                return Result.Failure<WorkItemDto>(new Error("Workflow.Main.ChildPending", "Complete the child workflow before approving its parent."));
        }

        var (outcomeError, outcome) = await LoadOutcomeAsync(item, request.ActionTaken, request.Comment, cancellationToken);
        if (outcomeError is not null)
            return Result.Failure<WorkItemDto>(outcomeError);

        var isRedirect = WorkflowOutcomeKeys.IsRedirect(request.ActionTaken, outcome?.ResultValue);
        if (isRedirect)
            return await RedirectAsync(item, request, cancellationToken);

        var formInstance = await _instances.GetByIdAsync(item.WorkflowInstanceId, cancellationToken);
        if (formInstance is not null && formInstance.Status != WorkflowInstanceStatus.Running)
            return Result.Failure<WorkItemDto>(WorkflowErrors.Instance.NotRunning);
        var formActivity = await _activities.GetByIdAsync(item.ActivityInstanceId, cancellationToken);
        var formVersion = formInstance is null ? null : await _versions.GetByIdWithProjectionAsync(formInstance.PinnedWorkflowVersionId, cancellationToken);
        var formDefinition = formVersion?.Activities.FirstOrDefault(a => a.NodeKey == formActivity?.ActivityNodeKey);
        try
        {
            var formConfig = WorkflowTaskForm.Parse(formDefinition?.ConfigurationJson);
            var binding = IntegrationJson.Read<BusinessActivityConfiguration>(formDefinition?.ConfigurationJson ?? "{}");
            if (binding.FormSteps.Count > 0 && !request.ActionTaken.Equals("REJECT", StringComparison.OrdinalIgnoreCase))
            {
                var processed = await SubmitFormStepAsync(item, request, binding, formVersion!, formDefinition!, cancellationToken);
                if (processed.IsFailure) return Result.Failure<WorkItemDto>(processed.Error);
                if (!processed.Value) return Result.Success(await _assembler.ToDtoAsync(item, true, cancellationToken));
            }
            else if (binding.FormSteps.Count > 0) { /* Reject does not submit or evaluate a form. */ }
            else if (binding.FormDefinitionId is Guid formId)
            {
                // Rejection keeps its existing comment/rework behavior; it does not complete a form.
                if (!request.ActionTaken.Equals("REJECT", StringComparison.OrdinalIgnoreCase))
                {
                    if (_forms is null || binding.FormVersionNo is not > 0)
                        return Result.Failure<WorkItemDto>(new Error("Workflow.Form.Invalid", "The selected form version is unavailable."));
                    var submitted = await _forms.SubmitAsync(new FormSubmitRequest
                    {
                        FormId = formId, VersionNo = binding.FormVersionNo.Value,
                        ContextType = "WorkItem", ContextId = item.Id.ToString(), ClientSubmissionId = item.Id,
                        Answers = (request.FormValues ?? []).ToDictionary(x => x.Key, x => (object?)x.Value)
                    }, cancellationToken);
                    if (submitted.IsFailure) return Result.Failure<WorkItemDto>(submitted.Error);
                    // A retry must use the answers actually recorded, including normalized media values.
                    var recorded = await _forms.GetLatestByContextAsync(formId, "WorkItem", item.Id.ToString(), cancellationToken);
                    if (recorded is null) return Result.Failure<WorkItemDto>(new Error("Workflow.Form.Invalid", "The form submission could not be read. Retry the action."));
                    item.SetFormData(System.Text.Json.JsonSerializer.Serialize(recorded.Answers));
                }
            }
            else
            {
                var formError = WorkflowTaskForm.Validate(formConfig, request.FormValues ?? []);
                if (formError is not null) return Result.Failure<WorkItemDto>(new Error("Workflow.Form.Invalid", formError));
                item.SetFormData(System.Text.Json.JsonSerializer.Serialize(request.FormValues ?? []));
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException or InvalidOperationException or OverflowException)
        { return Result.Failure<WorkItemDto>(new Error("Workflow.Form.Routing", "The form or routing configuration is invalid: " + ex.Message)); }

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

    private async Task<Result<bool>> SubmitFormStepAsync(WorkItem item, CompleteWorkItemCommand request,
        BusinessActivityConfiguration binding, WorkflowVersion version, ActivityDefinition definition, CancellationToken ct)
    {
        var progress = ActivityFormProgress.Read(item.FormProgressJson, binding.FormSteps, item.Id);
        if (request.FormVisitId is not Guid visitId)
            return Result.Failure<bool>(new Error("Workflow.Form.VisitRequired", "Refresh this task before submitting its active form."));
        if (progress.Visits.Any(v => v.VisitId == visitId && v.ActorId == request.UserId)) return Result.Success(false);
        if (visitId != progress.VisitId || progress.ExitNodeKey is not null)
            return Result.Failure<bool>(new Error("Workflow.Form.StaleVisit", "This form is no longer active. Refresh the task."));
        var step = binding.FormSteps.Single(s => s.Id == progress.ActiveStepId);
        if (_forms is null || await _forms.GetVersionSchemaAsync(step.FormId, step.VersionNo, ct) is null)
            return Result.Failure<bool>(new Error("Workflow.Form.Invalid", "The selected form version is unavailable."));
        var fields = await _forms.GetFieldsAsync(step.FormId, step.VersionNo, ct);
        // Check every possible route before recording an immutable submission.
        foreach (var destination in step.Rules.Select(r => r.Destination).Append(step.Otherwise))
            WorkflowFormRouting.ResolveDestination(binding.FormSteps, step, destination, version, definition.NodeKey);
        foreach (var rule in step.Rules)
        {
            var errors = WorkflowFormRouting.Validate(rule.When, fields);
            if (errors.Count > 0) return Result.Failure<bool>(new Error("Workflow.Form.Routing", errors[0]));
        }
        var context = item.Id + ":" + visitId;
        var submitted = await _forms.SubmitAsync(new FormSubmitRequest
        {
            FormId = step.FormId, VersionNo = step.VersionNo, ContextType = "WorkItem", ContextId = context,
            ClientSubmissionId = visitId, Answers = (request.FormValues ?? []).ToDictionary(x => x.Key, x => (object?)x.Value)
        }, ct);
        if (submitted.IsFailure) return Result.Failure<bool>(submitted.Error);
        var recorded = await _forms.GetLatestByContextAsync(step.FormId, "WorkItem", context, ct);
        if (recorded is null) return Result.Failure<bool>(new Error("Workflow.Form.Invalid", "The submission could not be read. Retry the action."));
        var decision = WorkflowFormRouting.Decide(step, fields, recorded.Answers);
        var target = WorkflowFormRouting.ResolveDestination(binding.FormSteps, step, decision.Destination, version, definition.NodeKey);
        var now = DateTime.UtcNow;
        progress.Visits.Add(new ActivityFormVisit
        {
            VisitId = visitId, StepId = step.Id, FormId = step.FormId, VersionNo = step.VersionNo, SubmissionId = recorded.SubmissionId,
            ActorId = request.UserId, SubmittedAt = now, RuleId = decision.RuleId, Destination = decision.Destination,
            Answers = recorded.Answers.ToDictionary(x => x.Key, x => x.Value)
        });
        if (target.FormStepId is not null) { progress.ActiveStepId = target.FormStepId; progress.VisitId = Guid.NewGuid(); }
        else progress.ExitNodeKey = target.NodeKey;
        item.SetFormProgress(System.Text.Json.JsonSerializer.Serialize(progress));
        if (target.FormStepId is null) item.SetFormData(System.Text.Json.JsonSerializer.Serialize(recorded.Answers));
        await _events.AppendAsync(item.OrganizationId, item.WorkflowInstanceId, WorkflowEventType.FormSubmitted, now,
            activityNodeKey: definition.NodeKey, actorUserId: request.UserId,
            payloadJson: System.Text.Json.JsonSerializer.Serialize(new { workItemId = item.Id, comment = request.Comment, visitId, stepId = step.Id, formId = step.FormId, step.VersionNo, submissionId = recorded.SubmissionId, ruleId = decision.RuleId, fallback = decision.RuleId is null, destination = decision.Destination }), cancellationToken: ct);
        if (target.FormStepId is not null) await _workItemRepo.SaveChangesAsync(ct);
        return Result.Success(target.FormStepId is null);
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
