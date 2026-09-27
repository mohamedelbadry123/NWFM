namespace Workflow.Infrastructure.Services;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;
using Workflow.Infrastructure.Persistence;
using Workflow.Application.Integrations;

/// <summary>
/// Generic workflow engine. Handles Start, UserTask, ExclusiveGateway, ServiceTask,
/// Timer, NotificationTask, ParallelGateway, InclusiveGateway, JoinGateway,
/// CallActivity, ScriptTask, WaitEvent, and End.
/// Never executes Draft versions.
/// </summary>
internal sealed partial class WorkflowRuntimeEngine : IWorkflowRuntimeEngine
{
    private readonly WorkflowDbContext _db;
    private readonly IWorkflowBindingRepository _bindingRepo;
    private readonly IWorkflowVersionResolver _versionResolver;
    private readonly IWorkflowDefinitionRepository _definitionRepo;
    private readonly IWorkflowVersionRepository _versionRepo;
    private readonly IWorkflowInstanceRepository _instanceRepo;
    private readonly IActivityInstanceRepository _activityRepo;
    private readonly IWorkItemRepository _workItemRepo;
    private readonly IWorkItemCandidateRepository _candidateRepo;
    private readonly IWorkflowVariableRepository _variableRepo;
    private readonly IWorkflowTransitionEvaluator _transitionEvaluator;
    private readonly IWorkflowAssignmentResolver _assignmentResolver;
    private readonly IWorkflowCandidateFactory _candidateFactory;
    private readonly ISlaPolicyRepository _slaRepo;
    private readonly IBusinessCalendarService _calendarService;
    private readonly ITransitionInstanceRepository _transitionInstanceRepo;
    private readonly IWorkflowEventAppender _events;
    private readonly IWorkflowEventRepository _eventRepo;
    private readonly IWorkflowActionRegistry _actionRegistry;
    private readonly IWorkflowIncidentService _incidentService;
    private readonly IWorkflowTimerService _timerService;
    private readonly IWorkflowTimerRepository _timerRepo;
    private readonly IWorkflowNotificationPublisher _notificationPublisher;
    private readonly IWorkflowExecutionTokenRepository _tokenRepo;
    private readonly IWorkflowOutcomeDispatcher _outcomeDispatcher;
    private readonly IWorkflowRequestProjector _requestProjector;
    private readonly IWorkflowIntegrationRuntime? _integrations;
    private readonly WorkflowActivityEvents? _activityEvents;

    public WorkflowRuntimeEngine(
        WorkflowDbContext db,
        IWorkflowBindingRepository bindingRepo,
        IWorkflowVersionResolver versionResolver,
        IWorkflowDefinitionRepository definitionRepo,
        IWorkflowVersionRepository versionRepo,
        IWorkflowInstanceRepository instanceRepo,
        IActivityInstanceRepository activityRepo,
        IWorkItemRepository workItemRepo,
        IWorkItemCandidateRepository candidateRepo,
        IWorkflowVariableRepository variableRepo,
        IWorkflowTransitionEvaluator transitionEvaluator,
        IWorkflowAssignmentResolver assignmentResolver,
        IWorkflowCandidateFactory candidateFactory,
        ISlaPolicyRepository slaRepo,
        IBusinessCalendarService calendarService,
        ITransitionInstanceRepository transitionInstanceRepo,
        IWorkflowEventAppender events,
        IWorkflowEventRepository eventRepo,
        IWorkflowActionRegistry actionRegistry,
        IWorkflowIncidentService incidentService,
        IWorkflowTimerService timerService,
        IWorkflowTimerRepository timerRepo,
        IWorkflowNotificationPublisher notificationPublisher,
        IWorkflowExecutionTokenRepository tokenRepo,
        IWorkflowOutcomeDispatcher outcomeDispatcher,
        IWorkflowRequestProjector requestProjector,
        IWorkflowIntegrationRuntime? integrations = null, WorkflowActivityEvents? activityEvents = null)
    {
        _db                     = db;
        _bindingRepo            = bindingRepo;
        _versionResolver        = versionResolver;
        _definitionRepo         = definitionRepo;
        _versionRepo            = versionRepo;
        _instanceRepo           = instanceRepo;
        _activityRepo           = activityRepo;
        _workItemRepo           = workItemRepo;
        _candidateRepo          = candidateRepo;
        _variableRepo           = variableRepo;
        _transitionEvaluator    = transitionEvaluator;
        _assignmentResolver     = assignmentResolver;
        _candidateFactory       = candidateFactory;
        _slaRepo                = slaRepo;
        _calendarService        = calendarService;
        _transitionInstanceRepo = transitionInstanceRepo;
        _events                 = events;
        _eventRepo              = eventRepo;
        _actionRegistry         = actionRegistry;
        _incidentService        = incidentService;
        _timerService           = timerService;
        _timerRepo              = timerRepo;
        _notificationPublisher  = notificationPublisher;
        _tokenRepo              = tokenRepo;
        _outcomeDispatcher      = outcomeDispatcher;
        _requestProjector       = requestProjector;
        _integrations           = integrations;
        _activityEvents = activityEvents;
    }

    public Task<Result<WorkflowInstance>> StartAsync(Guid organizationId, Guid workflowBindingId, string businessEntityId,
        string idempotencyKey, DateTime now, string? correlationId = null, Guid? startedByUserId = null,
        Guid? parentInstanceId = null, string? parentActivityNodeKey = null, Guid? pinnedWorkflowVersionId = null,
        CancellationToken cancellationToken = default)
        => StartCoreAsync(organizationId, workflowBindingId, businessEntityId, idempotencyKey, now, correlationId,
            startedByUserId, parentInstanceId, parentActivityNodeKey, pinnedWorkflowVersionId, cancellationToken);

    private async Task<Result<WorkflowInstance>> StartCoreAsync(
        Guid organizationId,
        Guid workflowBindingId,
        string businessEntityId,
        string idempotencyKey,
        DateTime now,
        string? correlationId = null,
        Guid? startedByUserId = null,
        Guid? parentInstanceId = null,
        string? parentActivityNodeKey = null,
        Guid? pinnedWorkflowVersionId = null,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, object?>? initialVariables = null,
        Guid? parentActivityInstanceId = null)
    {
        var existingInstance = await _instanceRepo.GetByIdempotencyKeyAsync(organizationId, idempotencyKey, cancellationToken);
        if (existingInstance is not null) return Result.Success(existingInstance);
        var binding = await _bindingRepo.GetByIdAsync(workflowBindingId, cancellationToken);
        if (binding is null)
            return Result.Failure<WorkflowInstance>(WorkflowErrors.Binding.NotFound);

        WorkflowVersion version;
        if (pinnedWorkflowVersionId.HasValue)
        {
            // Global published versions are not ITenantAware. IgnoreQueryFilters is required so a
            // tenant-scoped runtime request can load the pinned definition version it started with.
            var pinned = await _versionRepo.GetByIdWithProjectionAsync(
                pinnedWorkflowVersionId.Value, cancellationToken);
            if (pinned is null)
                return Result.Failure<WorkflowInstance>(WorkflowErrors.Version.NotFound);
            if (pinned.Status != WorkflowVersionStatus.Published && !(parentActivityInstanceId.HasValue && pinned.Status == WorkflowVersionStatus.Retired))
                return Result.Failure<WorkflowInstance>(WorkflowErrors.Version.NotPublished);
            version = pinned;
        }
        else
        {
            var versionResult = await _versionResolver.ResolveAsync(binding, cancellationToken);
            if (versionResult.IsFailure) return Result.Failure<WorkflowInstance>(versionResult.Error);
            version = versionResult.Value;
        }

        var startActivity = version.Activities.FirstOrDefault(a => a.ActivityType == ActivityType.Start);
        if (startActivity is null)
            return Result.Failure<WorkflowInstance>(WorkflowErrors.Instance.NoStartActivity);

        var instance = WorkflowInstance.Start(
            organizationId, workflowBindingId, version.Id, idempotencyKey,
            businessEntityId, startActivity.NodeKey, now, correlationId, startedByUserId,
            parentInstanceId, parentActivityNodeKey);
        instance.AttachParentActivity(parentActivityInstanceId);
        if (parentInstanceId is Guid parentId)
        {
            var parentContext = await _instanceRepo.GetByIdAsync(parentId, cancellationToken);
            if (parentContext is null || !await WorkflowTreeGuard.CanRunAsync(_db, parentId, cancellationToken))
                return Result.Failure<WorkflowInstance>(WorkflowErrors.Instance.NotRunning);
            instance.SetExecutionContext(parentContext.GeographyJson, parentContext.IsDemo);
        }
        else if (version.WorkspaceJson is not null)
        {
            var scope = JsonSerializer.Deserialize<WorkflowWorkspaceDefinition>(version.WorkspaceJson, IntegrationJson.Options)!;
            if (scope.Kind != "Main") return Result.Failure<WorkflowInstance>(new Error("Workflow.Child.Start", "Child workflows must be started by their parent."));
            instance.SetExecutionContext(JsonSerializer.Serialize(new WorkflowGeography(scope.ClusterCode!, scope.RegionCode!, scope.CityCode!), IntegrationJson.Options), binding.IsDemo);
        }

        await _instanceRepo.AddAsync(instance, cancellationToken);
        foreach (var variable in version.Variables.Where(v => v.DefaultValue is not null))
        {
            var value = variable.DataType == VariableDataType.String ? JsonSerializer.Serialize(variable.DefaultValue) : variable.DefaultValue!;
            try { using var parsed = JsonDocument.Parse(value); }
            catch (JsonException) { value = JsonSerializer.Serialize(variable.DefaultValue); }
            await UpsertVariableAsync(instance, variable.VariableKey, value, variable.DataType, now, cancellationToken);
        }
        if (initialVariables is not null)
            foreach (var (key, value) in initialVariables)
                await UpsertVariableAsync(instance, key, JsonSerializer.Serialize(value), VariableDataType.Json, now, cancellationToken);

        await UpsertVariableAsync(instance, "BusinessEntityId", JsonSerializer.Serialize(businessEntityId),
            VariableDataType.String, now, cancellationToken);
        await UpsertVariableAsync(instance, "OrganizationId", JsonSerializer.Serialize(organizationId.ToString()),
            VariableDataType.String, now, cancellationToken);
        await UpsertVariableAsync(instance, "CorrelationId", JsonSerializer.Serialize(correlationId ?? instance.Id.ToString("N")),
            VariableDataType.String, now, cancellationToken);
        if (startedByUserId is Guid starter && starter != Guid.Empty)
        {
            await UpsertVariableAsync(instance, "StartedByUserId", JsonSerializer.Serialize(starter.ToString()),
                VariableDataType.String, now, cancellationToken);
            await UpsertVariableAsync(instance, "ActorUserId", JsonSerializer.Serialize(starter.ToString()),
                VariableDataType.String, now, cancellationToken);
        }

        await _events.AppendAsync(
            organizationId, instance.Id, WorkflowEventType.InstanceStarted, now,
            startActivity.NodeKey, startedByUserId, cancellationToken: cancellationToken);

        var advanceResult = await AdvanceFromNodeAsync(
            instance, version, startActivity.NodeKey, now, cancellationToken);

        if (advanceResult.IsFailure)
        {
            instance.Fail(advanceResult.Error.Message, now);
            await _db.SaveChangesAsync(cancellationToken);
            await ProjectRequestAsync(instance, binding, now, cancellationToken);
            return Result.Failure<WorkflowInstance>(advanceResult.Error);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await ProjectRequestAsync(instance, binding, now, cancellationToken);
        return Result.Success(instance);
    }

    public async Task<Result> AdvanceAsync(
        Guid workflowInstanceId,
        Guid completedWorkItemId,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var instance = await _instanceRepo.GetByIdAsync(workflowInstanceId, cancellationToken);
        if (instance is null)
            return Result.Failure(WorkflowErrors.Instance.NotFound);

        if (instance.Status != WorkflowInstanceStatus.Running
            && !(completedWorkItemId == Guid.Empty && instance.Status == WorkflowInstanceStatus.Failed))
            return Result.Failure(WorkflowErrors.Instance.NotRunning);

        if (completedWorkItemId != Guid.Empty && !await WorkflowTreeGuard.CanRunAsync(_db, instance.Id, cancellationToken))
            return Result.Failure(WorkflowErrors.Instance.NotRunning);

        var version = await LoadPinnedVersionAsync(instance.PinnedWorkflowVersionId, cancellationToken);
        if (version is null)
            return Result.Failure(WorkflowErrors.Version.NotFound);

        WorkItem? completedWorkItem = null;
        if (completedWorkItemId != Guid.Empty)
        {
            completedWorkItem = await _workItemRepo.GetByIdAsync(completedWorkItemId, cancellationToken);
            if (completedWorkItem is null || completedWorkItem.WorkflowInstanceId != workflowInstanceId)
                return Result.Failure(WorkflowErrors.WorkItem.NotFound);

            if (completedWorkItem.ActionTaken is not null)
            {
                await UpsertVariableAsync(
                    instance, "LastActionTaken",
                    JsonSerializer.Serialize(completedWorkItem.ActionTaken),
                    VariableDataType.String, now, cancellationToken);
                await UpsertVariableAsync(
                    instance, "OutcomeKey",
                    JsonSerializer.Serialize(completedWorkItem.ActionTaken),
                    VariableDataType.String, now, cancellationToken);
                // Designer conditions use the concise `outcome` variable (for example,
                // outcome == 'APPROVE'). Keep the canonical integration key as well.
                await UpsertVariableAsync(
                    instance, "outcome",
                    JsonSerializer.Serialize(completedWorkItem.ActionTaken),
                    VariableDataType.String, now, cancellationToken);
            }

            if (completedWorkItem.CompletedByUserId is Guid completedBy && completedBy != Guid.Empty)
            {
                await UpsertVariableAsync(
                    instance, "CompletedByUserId",
                    JsonSerializer.Serialize(completedBy.ToString()),
                    VariableDataType.String, now, cancellationToken);
                await UpsertVariableAsync(
                    instance, "ActorUserId",
                    JsonSerializer.Serialize(completedBy.ToString()),
                    VariableDataType.String, now, cancellationToken);
            }
        }

        var activityInstances = await _activityRepo.GetByInstanceIdAsync(workflowInstanceId, cancellationToken);
        var currentAI = completedWorkItem is null
            ? activityInstances.LastOrDefault(a =>
                a.ActivityNodeKey == instance.CurrentActivityNodeKey
                && a.Status == ActivityInstanceStatus.Active)
            : activityInstances.FirstOrDefault(a => a.Id == completedWorkItem.ActivityInstanceId);
        var currentNodeKey = currentAI?.ActivityNodeKey ?? instance.CurrentActivityNodeKey;
        var currentActivity = version.Activities.FirstOrDefault(a => a.NodeKey == currentNodeKey);
        if (currentActivity is null)
            return Result.Failure(WorkflowErrors.Instance.NoOutgoingTransition);
        if (completedWorkItem is not null && currentAI?.Status == ActivityInstanceStatus.Completed)
            return Result.Success();
        if (completedWorkItem is not null && currentAI?.Phase != "WaitingForOutcomeEvents")
        {
            try
            {
                var taskConfig = Workflow.Application.Helpers.WorkflowTaskForm.Parse(currentActivity.ConfigurationJson);
                var fields = JsonSerializer.Deserialize<Dictionary<string, object?>>(completedWorkItem.FormDataJson ?? "{}") ?? [];
                fields["outcome"] = completedWorkItem.ActionTaken;
                fields["comment"] = completedWorkItem.CommentText;
                var mapped = IntegrationValueMapper.Map(JsonSerializer.Serialize(new { output = fields }), Workflow.Application.Helpers.WorkflowTaskForm.Mappings(taskConfig.OutputMappingJson));
                foreach (var (key, value) in mapped) await UpsertVariableAsync(instance, key, JsonSerializer.Serialize(value), VariableDataType.Json, now, cancellationToken);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                currentAI?.Fail(ex.Message, now); instance.Fail(ex.Message, now);
                if (_activityEvents is not null && currentAI is not null) await _activityEvents.QueueAsync(instance, currentActivity, currentAI, "OnFailure", "failure", cancellationToken);
                await _db.SaveChangesAsync(cancellationToken); return Result.Failure(new Error("Workflow.Form.Mapping", ex.Message));
            }
        }

        // An explicit retry re-enters the failed activity. It must never complete the
        // activity or traverse its outgoing edge before the external action succeeds.
        if (completedWorkItemId == Guid.Empty)
        {
            var failedActivity = activityInstances
                .Where(a => a.ActivityNodeKey == currentNodeKey)
                .OrderByDescending(a => a.StartedAt)
                .FirstOrDefault();
            if (failedActivity?.Status != ActivityInstanceStatus.Failed
                || currentActivity.ActivityType is not (ActivityType.ServiceTask or ActivityType.UserTask))
                return Result.Failure(new Error("Workflow.Activity.NotRetryable",
                    "Only a failed service activity can be retried through this operation."));
            if (currentActivity.ActionKey == "http.request")
                return Result.Failure(new Error("Workflow.Activity.UseOperationReplay", "Retry this API call from Integrations so its original operation identifier is preserved."));

            instance.Resume(now);
            if (currentActivity.ActivityType == ActivityType.UserTask)
            {
                failedActivity.Reopen();
                var priorWorkItem = await _db.WorkItems.FirstOrDefaultAsync(w => w.ActivityInstanceId == failedActivity.Id && w.Status == WorkItemStatus.Completed, cancellationToken);
                if (priorWorkItem is not null) return await AdvanceAsync(instance.Id, priorWorkItem.Id, now, cancellationToken);
            }
            var retryResult = await AdvanceFromNodeAsync(
                instance, version, currentNodeKey, now, cancellationToken,
                failedActivity.ExecutionTokenId is Guid failedTokenId ? await _tokenRepo.GetByIdAsync(failedTokenId, cancellationToken) : null, currentActivity.ActivityType == ActivityType.UserTask ? failedActivity : null);
            if (retryResult.IsFailure)
                instance.Fail(retryResult.Error.Message, now);
            await _db.SaveChangesAsync(cancellationToken);
            var retryBinding = await _bindingRepo.GetByIdAsync(instance.WorkflowBindingId, cancellationToken);
            await ProjectRequestAsync(instance, retryBinding, now, cancellationToken);
            return retryResult;
        }

        WorkflowExecutionToken? activeToken = currentAI?.ExecutionTokenId is Guid tokenId
            ? await _tokenRepo.GetByIdAsync(tokenId, cancellationToken) : null;
        if (completedWorkItem is not null && activeToken is null)
        {
            var incomingBranchKeys = version.Transitions
                .Where(t => t.ToActivityDefinitionId == currentActivity.Id)
                .Select(t => t.TransitionKey)
                .ToHashSet(StringComparer.Ordinal);
            activeToken = (await _tokenRepo.GetByInstanceIdAsync(instance.Id, cancellationToken))
                .FirstOrDefault(t => t.Status == ExecutionTokenStatus.Active
                    && incomingBranchKeys.Contains(t.BranchKey));
        }

        if (currentAI is not null)
        {
            if (_activityEvents is not null && completedWorkItem is not null)
            {
                currentAI.SetPendingOutcome(completedWorkItem.ActionTaken);
                var actionTrigger = completedWorkItem.ActionTaken?.ToUpperInvariant() switch { "APPROVE" => "OnApprove", "REJECT" => "OnReject", _ => null };
                foreach (var eventTrigger in actionTrigger is null ? new[] { "OnComplete" } : new[] { actionTrigger, "OnComplete" })
                {
                    var queued = await _activityEvents.QueueAsync(instance, currentActivity, currentAI, eventTrigger, completedWorkItem.Id.ToString("N"), cancellationToken);
                    if (queued.IsFailure) return Result.Failure(queued.Error);
                    if (!queued.Value) { currentAI.SetPhase("WaitingForOutcomeEvents"); await _db.SaveChangesAsync(cancellationToken); return Result.Success(); }
                }
                if (await _db.IntegrationJobs.AnyAsync(j => j.ActivityInstanceId == currentAI.Id && j.IsActivityEvent && j.Required && j.Status != "Completed", cancellationToken))
                { currentAI.SetPhase("WaitingForOutcomeEvents"); await _db.SaveChangesAsync(cancellationToken); return Result.Success(); }
            }
            foreach (var trigger in new[] { ActionExecutionTrigger.OnOutcome, ActionExecutionTrigger.OnComplete })
            {
                var hooks = await ExecuteHooksAsync(instance, currentActivity, currentAI, trigger, completedWorkItem?.ActionTaken, now, cancellationToken);
                if (hooks.IsFailure) { await _db.SaveChangesAsync(cancellationToken); return hooks; }
            }
        }
        currentAI?.Complete(now);

        await _events.AppendAsync(
            instance.OrganizationId, instance.Id, WorkflowEventType.ActivityCompleted, now,
            currentNodeKey, cancellationToken: cancellationToken);

        // Leave the wait-state node (UserTask). Re-entering it would create another work item
        // and never reach the next gateway — same pattern as ResumeFromTimerAsync.
        var outgoing = version.Transitions
            .Where(t => t.FromActivityDefinitionId == currentActivity.Id)
            .ToList();
        var business = IntegrationJson.Read<Workflow.Application.Workspace.BusinessActivityConfiguration>(currentActivity.ConfigurationJson);
        if (completedWorkItem?.ActionTaken?.Equals("reject", StringComparison.OrdinalIgnoreCase) == true && !string.IsNullOrWhiteSpace(business.RejectTargetNodeKey))
        {
            var target = version.Activities.FirstOrDefault(a => a.NodeKey == business.RejectTargetNodeKey);
            if (target is null) return Result.Failure(WorkflowErrors.Instance.NoOutgoingTransition);
            var rework = WorkflowTransition.Create(version.Id, currentActivity.Id, target.Id, currentNodeKey + ":rework", 0, now);
            await RecordTransitionTakenAsync(instance, currentNodeKey, target.NodeKey, rework, now, cancellationToken);
            instance.AdvanceTo(target.NodeKey, now);
            var reworkResult = await AdvanceFromNodeAsync(instance, version, target.NodeKey, now, cancellationToken, activeToken);
            await _db.SaveChangesAsync(cancellationToken);
            await ProjectRequestAsync(instance, await _bindingRepo.GetByIdAsync(instance.WorkflowBindingId, cancellationToken), now, cancellationToken);
            return reworkResult;
        }
        var variables = await _variableRepo.GetByInstanceIdAsync(instance.Id, cancellationToken);
        var selectedKey = _transitionEvaluator.Evaluate(outgoing, variables);
        var selected = selectedKey is null
            ? null
            : outgoing.FirstOrDefault(t => t.TransitionKey == selectedKey);
        var nextActivity = selected is null
            ? null
            : version.Activities.FirstOrDefault(a => a.Id == selected.ToActivityDefinitionId);
        if (selected is null || nextActivity is null)
            return Result.Failure(WorkflowErrors.Instance.NoOutgoingTransition);

        await RecordTransitionTakenAsync(instance, currentActivity.NodeKey, nextActivity.NodeKey,
            selected, now, cancellationToken);

        instance.AdvanceTo(nextActivity.NodeKey, now);

        var result = await AdvanceFromNodeAsync(
            instance, version, nextActivity.NodeKey, now, cancellationToken, activeToken);

        if (result.IsFailure)
            instance.Fail(result.Error.Message, now);

        await _db.SaveChangesAsync(cancellationToken);
        var binding = await _bindingRepo.GetByIdAsync(instance.WorkflowBindingId, cancellationToken);
        await ProjectRequestAsync(instance, binding, now, cancellationToken);
        return result;
    }

    public async Task<Result> ResumeFromTimerAsync(
        Guid timerId,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var timer = await _timerRepo.GetByIdAsync(timerId, cancellationToken);
        if (timer is null)
            return Result.Failure(WorkflowErrors.Timer.NotFound);

        // Idempotent if already processed
        if (timer.Status is WorkflowTimerStatus.Fired or WorkflowTimerStatus.Completed)
            return Result.Success();

        if (timer.Status != WorkflowTimerStatus.Pending)
            return Result.Failure(WorkflowErrors.Timer.NotPending);

        if (timer.TimerType == WorkflowTimerType.ExternalSignal)
            return Result.Failure(new Error("Workflow.Timer.ExternalSignalRequired",
                "An external-signal timer cannot be resumed by the clock."));

        if (timer.DueAt > now)
            return Result.Failure(WorkflowErrors.Timer.NotPending);

        var instance = await _instanceRepo.GetByIdAsync(timer.WorkflowInstanceId, cancellationToken);
        if (instance is null)
            return Result.Failure(WorkflowErrors.Instance.NotFound);

        if (!await WorkflowTreeGuard.CanRunAsync(_db, instance.Id, cancellationToken))
            return Result.Failure(WorkflowErrors.Instance.NotRunning);

        if (instance.Status == WorkflowInstanceStatus.Suspended)
            return Result.Failure(WorkflowErrors.Instance.NotRunning);

        if (instance.Status != WorkflowInstanceStatus.Running)
            return Result.Failure(WorkflowErrors.Instance.NotRunning);

        var version = await LoadPinnedVersionAsync(instance.PinnedWorkflowVersionId, cancellationToken);
        if (version is null)
            return Result.Failure(WorkflowErrors.Version.NotFound);

        timer.MarkFired(now);

        var activityInstances = await _activityRepo.GetByInstanceIdAsync(instance.Id, cancellationToken);
        var timerAi = activityInstances.FirstOrDefault(a => a.Id == timer.ActivityInstanceId)
                      ?? activityInstances.LastOrDefault(a =>
                          a.ActivityType == ActivityType.Timer
                          && a.Status == ActivityInstanceStatus.Active);

        timerAi?.Complete(now);

        var timerNodeKey = timerAi?.ActivityNodeKey ?? instance.CurrentActivityNodeKey;

        await _events.AppendAsync(
            instance.OrganizationId, instance.Id, WorkflowEventType.TimerFired, now,
            timerNodeKey, cancellationToken: cancellationToken);

        await _events.AppendAsync(
            instance.OrganizationId, instance.Id, WorkflowEventType.ActivityCompleted, now,
            timerNodeKey, cancellationToken: cancellationToken);

        // Advance past the timer node (next activity), not re-entering Timer
        var nextKey = GetSingleOutgoing(version, timerNodeKey);
        if (nextKey is null)
            return Result.Failure(WorkflowErrors.Instance.NoOutgoingTransition);

        instance.AdvanceTo(nextKey, now);

        var timerToken = timerAi?.ExecutionTokenId is Guid tokenId ? await _tokenRepo.GetByIdAsync(tokenId, cancellationToken) : null;
        var result = await AdvanceFromNodeAsync(instance, version, nextKey, now, cancellationToken, timerToken);
        if (result.IsFailure)
            instance.Fail(result.Error.Message, now);

        if (timer.Status == WorkflowTimerStatus.Fired)
            timer.Complete(now);

        await _db.SaveChangesAsync(cancellationToken);
        var binding = await _bindingRepo.GetByIdAsync(instance.WorkflowBindingId, cancellationToken);
        await ProjectRequestAsync(instance, binding, now, cancellationToken);
        return result;
    }

    public async Task<Result> ResumeFromExternalSignalAsync(
        Guid workflowInstanceId,
        string? signalKey,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var instance = await _instanceRepo.GetByIdAsync(workflowInstanceId, cancellationToken);
        if (instance is null)
            return Result.Failure(WorkflowErrors.Instance.NotFound);

        if (instance.Status != WorkflowInstanceStatus.Running)
            return Result.Failure(WorkflowErrors.Instance.NotRunning);

        var version = await LoadPinnedVersionAsync(instance.PinnedWorkflowVersionId, cancellationToken);
        if (version is null)
            return Result.Failure(WorkflowErrors.Version.NotFound);

        var active = (await _activityRepo.GetByInstanceIdAsync(instance.Id, cancellationToken))
            .Where(a => a.ActivityType == ActivityType.WaitEvent && a.Status == ActivityInstanceStatus.Active).ToList();
        if (active.Count == 0) return Result.Failure(new Error("Workflow.Event.NoActiveWait", "No active internal event wait exists."));
        if (string.IsNullOrWhiteSpace(signalKey)) return Result.Failure(new Error("Workflow.Event.KeyMismatch", "A non-empty matching signal key is required."));
        var matches = active.Where(ai => version.Activities.Any(a => a.NodeKey == ai.ActivityNodeKey
            && string.Equals(TryReadSignalKey(a.ConfigurationJson ?? "{}"), signalKey, StringComparison.OrdinalIgnoreCase)
            && !HasWebhookConnection(a.ConfigurationJson))).ToList();
        if (matches.Count != 1) return Result.Failure(new Error("Workflow.Event.KeyMismatch", "The signal must identify exactly one active internal event wait."));
        return await CompleteExternalActivityAsync(matches[0].Id, new Dictionary<string, object?>(), "received", null, now, cancellationToken);
    }

    private static bool HasWebhookConnection(string? json)
    {
        try { return IntegrationJson.Read<EventActivityConfiguration>(json).ConnectionId != Guid.Empty; }
        catch (JsonException) { return false; }
    }
    public async Task<Result> ResumeFromCallActivityAsync(
        Guid parentInstanceId,
        string callActivityNodeKey,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var instance = await _instanceRepo.GetByIdAsync(parentInstanceId, cancellationToken);
        if (instance is null)
            return Result.Failure(WorkflowErrors.Instance.NotFound);

        if (instance.Status != WorkflowInstanceStatus.Running)
            return Result.Failure(WorkflowErrors.Instance.NotRunning);

        var version = await LoadPinnedVersionAsync(instance.PinnedWorkflowVersionId, cancellationToken);
        if (version is null)
            return Result.Failure(WorkflowErrors.Version.NotFound);

        var callActivity = version.Activities.FirstOrDefault(a =>
            a.NodeKey == callActivityNodeKey && a.ActivityType is ActivityType.CallActivity or ActivityType.MainActivity);
        if (callActivity is null)
            return Result.Failure(WorkflowErrors.Instance.UnhandledActivityType);

        var activityInstances = await _activityRepo.GetByInstanceIdAsync(instance.Id, cancellationToken);
        var callAi = activityInstances.LastOrDefault(a =>
            a.ActivityNodeKey == callActivityNodeKey
            && a.ActivityType is ActivityType.CallActivity or ActivityType.MainActivity
            && a.Status == ActivityInstanceStatus.Active);
        if (callAi is null) return Result.Success(); // already resumed, including synchronous children
        if (callActivity.ActivityType == ActivityType.MainActivity && callAi.Phase is not ("WaitingForChild" or "ChildFailed")) return Result.Success();
        if (!await WorkflowTreeGuard.CanRunAsync(_db, instance.Id, cancellationToken)) return Result.Success();
        var child = await _db.WorkflowInstances.Where(c => c.ParentInstanceId == instance.Id
            && c.ParentActivityNodeKey == callActivityNodeKey
            && (c.ParentActivityInstanceId == callAi.Id || c.ParentActivityInstanceId == null))
            .OrderByDescending(c => c.StartedAt).FirstOrDefaultAsync(cancellationToken);
        if (child is null || child.Status is WorkflowInstanceStatus.Running or WorkflowInstanceStatus.Suspended or WorkflowInstanceStatus.Pending)
            return Result.Success();
        var config = ParseCallActivityConfig(callActivity.ConfigurationJson);
        var outputs = new Dictionary<string, object?>();
        if (child.Status == WorkflowInstanceStatus.Completed)
        {
            var childValues = BuildInputVariables(await _variableRepo.GetByInstanceIdAsync(child.Id, cancellationToken));
            try { outputs = IntegrationValueMapper.Map(JsonSerializer.Serialize(childValues), config.OutputMappings); }
            catch (InvalidOperationException ex)
            {
                if (callActivity.ActivityType != ActivityType.MainActivity)
                    return await CompleteExternalActivityAsync(callAi.Id, outputs, "error", ex.Message, now, cancellationToken);
                callAi.SetPhase("ChildFailed");
                if (_activityEvents is not null)
                    await _activityEvents.QueueAsync(instance, callActivity, callAi, "OnFailure", child.Id.ToString("N"), cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                return Result.Failure(new Error("Workflow.Child.OutputMapping", ex.Message));
            }
        }
        if (callActivity.ActivityType == ActivityType.MainActivity)
        {
            if (child.Status != WorkflowInstanceStatus.Completed)
            {
                if (_activityEvents is not null) await _activityEvents.QueueAsync(instance, callActivity, callAi, "OnFailure", child.Id.ToString("N"), cancellationToken);
                callAi.SetPhase("ChildFailed");
                await _db.SaveChangesAsync(cancellationToken);
                return Result.Success();
            }
            foreach (var (key, value) in outputs) await UpsertVariableAsync(instance, key, JsonSerializer.Serialize(value), VariableDataType.Json, now, cancellationToken);
            return await CreateApprovalTaskAsync(instance, callActivity, callAi, now, cancellationToken);
        }
        return await CompleteExternalActivityAsync(callAi.Id, outputs,
            child.Status == WorkflowInstanceStatus.Completed ? "success" : "error",
            child.Status == WorkflowInstanceStatus.Completed ? null : "The child workflow failed or was cancelled.", now, cancellationToken);
    }
    private async Task<Result> AdvanceFromNodeAsync(
        WorkflowInstance instance,
        WorkflowVersion version,
        string fromNodeKey,
        DateTime now,
        CancellationToken cancellationToken,
        WorkflowExecutionToken? activeToken = null, ActivityInstance? retryExecution = null)
    {
        var visited = new HashSet<string>();
        var currentNodeKey = fromNodeKey;
        var currentToken = activeToken;

        while (true)
        {
            if (instance.Status is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Failed or WorkflowInstanceStatus.Cancelled)
                return Result.Success();

            if (!visited.Add(currentNodeKey))
                return Result.Failure(WorkflowErrors.Instance.NoOutgoingTransition);

            var currentActivity = version.Activities.FirstOrDefault(a => a.NodeKey == currentNodeKey);
            if (currentActivity is null)
                return Result.Failure(WorkflowErrors.Instance.NoOutgoingTransition);

            switch (currentActivity.ActivityType)
            {
                case ActivityType.Start:
                {
                    var nextKey = GetSingleOutgoing(version, currentNodeKey);
                    if (nextKey is null) return Result.Failure(WorkflowErrors.Instance.NoOutgoingTransition);
                    instance.AdvanceTo(nextKey, now);
                    currentNodeKey = nextKey;
                    break;
                }

                case ActivityType.UserTask:
                {
                    var activityInst = retryExecution ?? await CreateActivityInstanceAsync(
                        instance, currentActivity, now, cancellationToken);
                    activityInst.AttachToken(currentToken?.Id);
                    activityInst.SetDeadline(await ResolveUserTaskDueAtAsync(instance.OrganizationId, currentActivity.ConfigurationJson, now, cancellationToken));
                    if (_activityEvents is not null)
                    {
                        var entry = await _activityEvents.QueueAsync(instance, currentActivity, activityInst, "OnEnter", "entry", cancellationToken);
                        if (entry.IsFailure) return Result.Failure(entry.Error);
                        if (!entry.Value) { activityInst.SetPhase("WaitingForEnterEvents"); instance.AdvanceTo(currentNodeKey, now); return Result.Success(); }
                    }

                    var hooks = await ExecuteHooksAsync(instance, currentActivity, activityInst, ActionExecutionTrigger.OnEnter, null, now, cancellationToken);
                    if (hooks.IsFailure) return hooks;

                    return await CreateApprovalTaskAsync(instance, currentActivity, activityInst, now, cancellationToken);
                }

                case ActivityType.ExclusiveGateway:
                {
                    var gatewayInst = await CreateActivityInstanceAsync(
                        instance, currentActivity, now, cancellationToken);

                    var outgoingTransitions = version.Transitions
                        .Where(t => t.FromActivityDefinitionId == currentActivity.Id)
                        .ToList();

                    var variables = await _variableRepo.GetByInstanceIdAsync(instance.Id, cancellationToken);
                    var transitionKey = _transitionEvaluator.Evaluate(outgoingTransitions, variables);

                    if (transitionKey is null)
                        return Result.Failure(WorkflowErrors.Instance.NoOutgoingTransition);

                    var selectedTransition = outgoingTransitions.First(t => t.TransitionKey == transitionKey);
                    var nextActivity = version.Activities.FirstOrDefault(
                        a => a.Id == selectedTransition.ToActivityDefinitionId);
                    if (nextActivity is null)
                        return Result.Failure(WorkflowErrors.Instance.NoOutgoingTransition);

                    await RecordTransitionTakenAsync(
                        instance, currentNodeKey, nextActivity.NodeKey, selectedTransition, now, cancellationToken);

                    gatewayInst.Complete(now);
                    await _events.AppendAsync(
                        instance.OrganizationId, instance.Id,
                        WorkflowEventType.ActivityCompleted, now, currentNodeKey,
                        cancellationToken: cancellationToken);

                    instance.AdvanceTo(nextActivity.NodeKey, now);
                    currentNodeKey = nextActivity.NodeKey;
                    break;
                }

                case ActivityType.ScriptTask:
                {
                    var scriptResult = await ExecuteScriptTaskAsync(
                        instance, version, currentActivity, now, cancellationToken);
                    if (scriptResult.IsFailure)
                        return scriptResult;

                    var nextKey = GetSingleOutgoing(version, currentNodeKey);
                    if (nextKey is null) return Result.Failure(WorkflowErrors.Instance.NoOutgoingTransition);
                    instance.AdvanceTo(nextKey, now);
                    currentNodeKey = nextKey;
                    break;
                }

                case ActivityType.WaitEvent:
                {
                    var waitInstance = await CreateActivityInstanceAsync(instance, currentActivity, now, cancellationToken);
                    waitInstance.AttachToken(currentToken?.Id);
                    EventActivityConfiguration eventConfig;
                    try { eventConfig = IntegrationJson.Read<EventActivityConfiguration>(currentActivity.ConfigurationJson); }
                    catch (JsonException) { eventConfig = new(); }
                    if (eventConfig.ConnectionId != Guid.Empty)
                    {
                        if (_integrations is null) return Result.Failure(new Error("Workflow.Integration.Unavailable", "Integration runtime is unavailable."));
                        var variables = BuildInputVariables(await _variableRepo.GetByInstanceIdAsync(instance.Id, cancellationToken));
                        var registration = await _integrations.RegisterWaitAsync(instance.Id, waitInstance.Id,
                            currentActivity.ConfigurationJson!, variables, cancellationToken);
                        if (registration.IsFailure) return registration;
                    }

                    await _events.AppendAsync(
                        instance.OrganizationId, instance.Id,
                        WorkflowEventType.ActivityStarted, now, currentNodeKey,
                        payloadJson: currentActivity.ConfigurationJson,
                        cancellationToken: cancellationToken);

                    instance.AdvanceTo(currentNodeKey, now);
                    return Result.Success(); // HALT — waiting for external signal
                }

                case ActivityType.ServiceTask:
                {
                    if (currentActivity.ActionKey == "http.request")
                        return await QueueIntegrationActivityAsync(instance, currentActivity, currentToken, "Http", now, cancellationToken);
                    var serviceResult = await ExecuteServiceTaskAsync(
                        instance, currentActivity, now, cancellationToken);
                    if (serviceResult.IsFailure)
                        return serviceResult;

                    var nextKey = GetSingleOutgoing(version, currentNodeKey);
                    if (nextKey is null) return Result.Failure(WorkflowErrors.Instance.NoOutgoingTransition);
                    instance.AdvanceTo(nextKey, now);
                    currentNodeKey = nextKey;
                    break;
                }

                case ActivityType.Timer:
                {
                    var activityInst = await CreateActivityInstanceAsync(
                        instance, currentActivity, now, cancellationToken);
                    activityInst.AttachToken(currentToken?.Id);

                    var timerConfig = ParseTimerConfig(currentActivity.ConfigurationJson);
                    var dueAt = ResolveTimerDueAt(timerConfig, now);

                    var scheduleResult = await _timerService.ScheduleAsync(
                        instance.OrganizationId,
                        instance.Id,
                        activityInst.Id,
                        timerConfig.TimerType,
                        dueAt,
                        now,
                        timerConfig.SignalKey,
                        cancellationToken);

                    if (scheduleResult.IsFailure)
                        return Result.Failure(scheduleResult.Error);

                    await _events.AppendAsync(
                        instance.OrganizationId, instance.Id,
                        WorkflowEventType.TimerScheduled, now, currentNodeKey,
                        payloadJson: $"{{\"timerId\":\"{scheduleResult.Value.Id}\",\"dueAt\":\"{dueAt:O}\"}}",
                        cancellationToken: cancellationToken);

                    await _events.AppendAsync(
                        instance.OrganizationId, instance.Id,
                        WorkflowEventType.ActivityStarted, now, currentNodeKey,
                        cancellationToken: cancellationToken);

                    instance.AdvanceTo(currentNodeKey, now);
                    return Result.Success(); // HALT — waiting for timer
                }

                case ActivityType.NotificationTask:
                {
                    var emailConfig = IntegrationJson.Read<EmailActivityConfiguration>(currentActivity.ConfigurationJson);
                    if (emailConfig.Channels.Contains("Email", StringComparison.OrdinalIgnoreCase))
                    {
                        if (emailConfig.Channels.Contains("InApp", StringComparison.OrdinalIgnoreCase))
                        {
                            var inAppVariables = BuildInputVariables(await _variableRepo.GetByInstanceIdAsync(instance.Id, cancellationToken));
                            await _notificationPublisher.PublishAsync(new WorkflowNotificationRequest(instance.OrganizationId,
                                emailConfig.TemplateKey, WorkflowNotificationChannel.InApp, inAppVariables,
                                instance.CorrelationId ?? instance.Id.ToString(), emailConfig.RecipientUserIds), cancellationToken);
                        }
                        return await QueueIntegrationActivityAsync(instance, currentActivity, currentToken, "Email", now, cancellationToken);
                    }
                    var notifResult = await ExecuteNotificationTaskAsync(
                        instance, currentActivity, now, cancellationToken);
                    if (notifResult.IsFailure)
                        return notifResult;

                    var nextKey = GetSingleOutgoing(version, currentNodeKey);
                    if (nextKey is null) return Result.Failure(WorkflowErrors.Instance.NoOutgoingTransition);
                    instance.AdvanceTo(nextKey, now);
                    currentNodeKey = nextKey;
                    break;
                }

                case ActivityType.ParallelGateway:
                {
                    var forkResult = await ExecuteParallelGatewayAsync(
                        instance, version, currentActivity, now, cancellationToken, currentToken);
                    return forkResult;
                }

                case ActivityType.InclusiveGateway:
                {
                    var inclusiveResult = await ExecuteInclusiveGatewayAsync(
                        instance, version, currentActivity, now, cancellationToken, currentToken);
                    return inclusiveResult;
                }

                case ActivityType.MainActivity:
                case ActivityType.CallActivity:
                {
                    var callResult = await ExecuteCallActivityAsync(
                        instance, version, currentActivity, now, cancellationToken, currentToken);
                    return callResult;
                }

                case ActivityType.JoinGateway:
                {
                    var joinResult = await ExecuteJoinGatewayAsync(
                        instance, version, currentActivity, currentToken, now, cancellationToken);

                    if (joinResult.Halt)
                        return joinResult.Result;

                    if (joinResult.Result.IsFailure)
                        return joinResult.Result;

                    currentToken = currentToken?.ParentTokenId is Guid parentTokenId ? await _tokenRepo.GetByIdAsync(parentTokenId, cancellationToken) : null;
                    currentNodeKey = joinResult.NextNodeKey!;
                    instance.AdvanceTo(currentNodeKey, now);
                    break;
                }

                case ActivityType.End:
                {
                    var endActivity = await CreateActivityInstanceAsync(instance, currentActivity, now, cancellationToken);
                    endActivity.AttachToken(currentToken?.Id);
                    endActivity.Complete(now);
                    if (currentToken is not null)
                    {
                        currentToken.Complete(now);
                        await _db.SaveChangesAsync(cancellationToken);
                        if ((await _tokenRepo.GetByInstanceIdAsync(instance.Id, cancellationToken)).Any(t => t.Status == ExecutionTokenStatus.Active))
                            return Result.Success();
                    }
                    instance.Complete(now);

                    await _events.AppendAsync(
                        instance.OrganizationId, instance.Id,
                        WorkflowEventType.InstanceCompleted, now, currentNodeKey,
                        cancellationToken: cancellationToken);

                    if (instance.ParentInstanceId is null) await DispatchOutcomeAsync(instance, now, cancellationToken);

                    // The durable child worker resumes asynchronous parents under
                    // their own lock. Avoid acquiring parent locks from child transactions.

                    return Result.Success();
                }

                default:
                    return Result.Failure(WorkflowErrors.Instance.UnhandledActivityType);
            }
        }
    }

    private async Task<Result> ExecuteServiceTaskAsync(
        WorkflowInstance instance,
        ActivityDefinition activity,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var actionKey = activity.ActionKey;
        if (string.IsNullOrWhiteSpace(actionKey))
            return Result.Failure(WorkflowErrors.Action.NotFound);

        var provider = _actionRegistry.Resolve(actionKey);
        if (provider is null)
            return Result.Failure(WorkflowErrors.Action.NotFound);

        var completedExecutions = (await _activityRepo.GetByInstanceIdAsync(instance.Id, cancellationToken))
            .Count(a => a.ActivityNodeKey == activity.NodeKey && a.Status == ActivityInstanceStatus.Completed);

        var activityInst = await CreateActivityInstanceAsync(instance, activity, now, cancellationToken);

        await _events.AppendAsync(
            instance.OrganizationId, instance.Id,
            WorkflowEventType.ActivityStarted, now, activity.NodeKey,
            cancellationToken: cancellationToken);

        var variables = await _variableRepo.GetByInstanceIdAsync(instance.Id, cancellationToken);
        var inputVars = BuildInputVariables(variables);

        // Retries share an operation key; a later successful traversal gets a new key.
        var idempotencyKey = $"{instance.Id}:{activity.NodeKey}:{completedExecutions + 1}";
        var context = new WorkflowActionExecutionContext(
            instance.OrganizationId,
            instance.Id,
            actionKey,
            inputVars,
            idempotencyKey,
            activity.ConfigurationJson,
            activityInst.Id);

        WorkflowActionExecutionResult execResult;
        try
        {
            execResult = await provider.ExecuteAsync(context, cancellationToken);
        }
        catch (Exception ex)
        {
            execResult = WorkflowActionExecutionResult.Failed(
                "Workflow.Action.UnhandledException", ex.Message, isRetryable: true);
        }

        if (!execResult.Success)
        {
            activityInst.Fail(execResult.ErrorMessage ?? "Service task failed", now);
            if (_activityEvents is not null) await _activityEvents.QueueAsync(instance, activity, activityInst, "OnFailure", "failure", cancellationToken);

            await _events.AppendAsync(
                instance.OrganizationId, instance.Id,
                WorkflowEventType.ActivityFailed, now, activity.NodeKey,
                payloadJson: $"{{\"errorCode\":\"{execResult.ErrorCode}\"}}",
                cancellationToken: cancellationToken);

            await _incidentService.OpenAsync(
                instance.OrganizationId,
                instance.Id,
                WorkflowIncidentType.ServiceTaskFailed,
                execResult.IsRetryable ? WorkflowIncidentSeverity.Medium : WorkflowIncidentSeverity.High,
                $"ServiceTask '{activity.Name}' failed",
                now,
                activityInst.Id,
                activity.NodeKey,
                execResult.ErrorCode,
                execResult.ErrorMessage,
                cancellationToken);

            await _events.AppendAsync(
                instance.OrganizationId, instance.Id,
                WorkflowEventType.IncidentOpened, now, activity.NodeKey,
                cancellationToken: cancellationToken);

            // Until durable automatic retries are introduced, all failures stop at
            // the failed node and use the existing explicit retry operation.
            instance.AdvanceTo(activity.NodeKey, now);
            instance.Fail(execResult.ErrorMessage ?? WorkflowErrors.Action.ExecutionFailed.Message, now);
            return Result.Failure(WorkflowErrors.Action.ExecutionFailed);
        }

        foreach (var (key, value) in execResult.OutputVariables)
        {
            await UpsertVariableAsync(
                instance, key,
                JsonSerializer.Serialize(value),
                VariableDataType.Json, now, cancellationToken);
        }

        activityInst.Complete(now);

        await _events.AppendAsync(
            instance.OrganizationId, instance.Id,
            WorkflowEventType.ServiceTaskExecuted, now, activity.NodeKey,
            cancellationToken: cancellationToken);

        await _events.AppendAsync(
            instance.OrganizationId, instance.Id,
            WorkflowEventType.ActivityCompleted, now, activity.NodeKey,
            cancellationToken: cancellationToken);

        return Result.Success();
    }

    private async Task<Result> ExecuteNotificationTaskAsync(
        WorkflowInstance instance,
        ActivityDefinition activity,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var activityInst = await CreateActivityInstanceAsync(instance, activity, now, cancellationToken);
        var config = ParseNotificationConfig(activity.ConfigurationJson);

        await _events.AppendAsync(
            instance.OrganizationId, instance.Id,
            WorkflowEventType.ActivityStarted, now, activity.NodeKey,
            cancellationToken: cancellationToken);

        var variables = await _variableRepo.GetByInstanceIdAsync(instance.Id, cancellationToken);
        var notifVars = BuildInputVariables(variables);

        try
        {
            await _notificationPublisher.PublishAsync(
                new WorkflowNotificationRequest(
                    instance.OrganizationId,
                    config.TemplateKey,
                    config.Channels,
                    notifVars,
                    instance.CorrelationId ?? instance.Id.ToString("N"),
                    config.RecipientUserIds),
                cancellationToken);

            await _events.AppendAsync(
                instance.OrganizationId, instance.Id,
                WorkflowEventType.NotificationQueued, now, activity.NodeKey,
                cancellationToken: cancellationToken);

            activityInst.Complete(now);

            await _events.AppendAsync(
                instance.OrganizationId, instance.Id,
                WorkflowEventType.ActivityCompleted, now, activity.NodeKey,
                cancellationToken: cancellationToken);

            return Result.Success();
        }
        catch (Exception ex)
        {
            await _events.AppendAsync(
                instance.OrganizationId, instance.Id,
                WorkflowEventType.NotificationFailed, now, activity.NodeKey,
                payloadJson: $"{{\"message\":{JsonSerializer.Serialize(ex.Message)}}}",
                cancellationToken: cancellationToken);

            await _incidentService.OpenAsync(
                instance.OrganizationId,
                instance.Id,
                WorkflowIncidentType.NotificationFailed,
                WorkflowIncidentSeverity.Medium,
                $"NotificationTask '{activity.Name}' failed",
                now,
                activityInst.Id,
                activity.NodeKey,
                "Workflow.Notification.Failed",
                ex.Message,
                cancellationToken);

            if (config.FailurePolicy == NotificationFailurePolicy.FailWorkflow)
            {
                activityInst.Fail(ex.Message, now);
                instance.Fail(ex.Message, now);
                if (_activityEvents is not null) await _activityEvents.QueueAsync(instance, activity, activityInst, "OnFailure", "failure", cancellationToken);
                return Result.Failure(new Error("Workflow.Notification.Failed", ex.Message));
            }

            // Continue / Retry (retry not implemented as background yet) — advance
            activityInst.Complete(now);
            await _events.AppendAsync(
                instance.OrganizationId, instance.Id,
                WorkflowEventType.ActivityCompleted, now, activity.NodeKey,
                cancellationToken: cancellationToken);

            return Result.Success();
        }
    }

    private async Task<Result> ExecuteParallelGatewayAsync(
        WorkflowInstance instance,
        WorkflowVersion version,
        ActivityDefinition currentActivity,
        DateTime now,
        CancellationToken cancellationToken, WorkflowExecutionToken? parentToken = null)
    {
        var activityInst = await CreateActivityInstanceAsync(instance, currentActivity, now, cancellationToken);
        activityInst.Complete(now);

        await _events.AppendAsync(
            instance.OrganizationId, instance.Id,
            WorkflowEventType.ActivityStarted, now, currentActivity.NodeKey,
            cancellationToken: cancellationToken);

        await _events.AppendAsync(
            instance.OrganizationId, instance.Id,
            WorkflowEventType.ActivityCompleted, now, currentActivity.NodeKey,
            cancellationToken: cancellationToken);

        var joinNodeKey = ResolveJoinNodeKey(version, currentActivity);

        var outgoingTransitions = version.Transitions
            .Where(t => t.FromActivityDefinitionId == currentActivity.Id)
            .OrderBy(t => t.Priority)
            .ToList();

        if (outgoingTransitions.Count == 0)
            return Result.Failure(WorkflowErrors.Instance.NoOutgoingTransition);

        var branches = new List<(WorkflowExecutionToken Token, string NextNodeKey)>();

        foreach (var transition in outgoingTransitions)
        {
            var nextActivity = version.Activities.FirstOrDefault(a => a.Id == transition.ToActivityDefinitionId);
            if (nextActivity is null)
                return Result.Failure(WorkflowErrors.Instance.NoOutgoingTransition);

            var token = WorkflowExecutionToken.Create(instance.OrganizationId, instance.Id,
                currentActivity.NodeKey, $"{activityInst.Id:N}:{branches.Count}", now,
                joinNodeKey, parentToken?.Id, activityInst.Id);
            await _tokenRepo.AddAsync(token, cancellationToken);

            await _events.AppendAsync(
                instance.OrganizationId, instance.Id,
                WorkflowEventType.ParallelBranchStarted, now, currentActivity.NodeKey,
                payloadJson: $"{{\"branchKey\":\"{transition.TransitionKey}\",\"tokenId\":\"{token.Id}\"}}",
                cancellationToken: cancellationToken);

            branches.Add((token, nextActivity.NodeKey));
        }

        instance.AdvanceTo(currentActivity.NodeKey, now);

        foreach (var (token, nextKey) in branches)
        {
            if (instance.Status is not WorkflowInstanceStatus.Running)
                break;

            if (token.Status != ExecutionTokenStatus.Active)
                continue;

            var branchResult = await AdvanceFromNodeAsync(
                instance, version, nextKey, now, cancellationToken, token);
            if (branchResult.IsFailure)
                return branchResult;
        }

        return Result.Success();
    }

    private async Task<Result> ExecuteInclusiveGatewayAsync(
        WorkflowInstance instance,
        WorkflowVersion version,
        ActivityDefinition currentActivity,
        DateTime now,
        CancellationToken cancellationToken, WorkflowExecutionToken? parentToken = null)
    {
        var activityInst = await CreateActivityInstanceAsync(instance, currentActivity, now, cancellationToken);
        activityInst.Complete(now);

        await _events.AppendAsync(
            instance.OrganizationId, instance.Id,
            WorkflowEventType.ActivityStarted, now, currentActivity.NodeKey,
            cancellationToken: cancellationToken);

        await _events.AppendAsync(
            instance.OrganizationId, instance.Id,
            WorkflowEventType.ActivityCompleted, now, currentActivity.NodeKey,
            cancellationToken: cancellationToken);

        var joinNodeKey = ResolveJoinNodeKey(version, currentActivity);

        var outgoingTransitions = version.Transitions
            .Where(t => t.FromActivityDefinitionId == currentActivity.Id)
            .OrderBy(t => t.Priority)
            .ToList();

        if (outgoingTransitions.Count == 0)
            return Result.Failure(WorkflowErrors.Instance.NoOutgoingTransition);

        var variables = await _variableRepo.GetByInstanceIdAsync(instance.Id, cancellationToken);
        var selected = _transitionEvaluator.EvaluateAllMatching(outgoingTransitions, variables);
        if (selected.Count == 0)
            return Result.Failure(WorkflowErrors.Instance.NoOutgoingTransition);

        var branches = new List<(WorkflowExecutionToken Token, string NextNodeKey)>();

        foreach (var transition in selected)
        {
            var nextActivity = version.Activities.FirstOrDefault(a => a.Id == transition.ToActivityDefinitionId);
            if (nextActivity is null)
                return Result.Failure(WorkflowErrors.Instance.NoOutgoingTransition);

            var token = WorkflowExecutionToken.Create(instance.OrganizationId, instance.Id,
                currentActivity.NodeKey, $"{activityInst.Id:N}:{branches.Count}", now,
                joinNodeKey, parentToken?.Id, activityInst.Id);
            await _tokenRepo.AddAsync(token, cancellationToken);

            await RecordTransitionTakenAsync(
                instance, currentActivity.NodeKey, nextActivity.NodeKey, transition, now, cancellationToken);

            await _events.AppendAsync(
                instance.OrganizationId, instance.Id,
                WorkflowEventType.ParallelBranchStarted, now, currentActivity.NodeKey,
                payloadJson: $"{{\"branchKey\":\"{transition.TransitionKey}\",\"tokenId\":\"{token.Id}\",\"inclusive\":true}}",
                cancellationToken: cancellationToken);

            branches.Add((token, nextActivity.NodeKey));
        }

        instance.AdvanceTo(currentActivity.NodeKey, now);

        foreach (var (token, nextKey) in branches)
        {
            if (instance.Status is not WorkflowInstanceStatus.Running)
                break;

            if (token.Status != ExecutionTokenStatus.Active)
                continue;

            var branchResult = await AdvanceFromNodeAsync(
                instance, version, nextKey, now, cancellationToken, token);
            if (branchResult.IsFailure)
                return branchResult;
        }

        return Result.Success();
    }

    private async Task<Result> ExecuteCallActivityAsync(
        WorkflowInstance instance,
        WorkflowVersion version,
        ActivityDefinition currentActivity,
        DateTime now,
        CancellationToken cancellationToken,
        WorkflowExecutionToken? activeToken = null, ActivityInstance? existingCall = null)
    {
        var config = ParseCallActivityConfig(currentActivity.ConfigurationJson);
        var childPins = JsonSerializer.Deserialize<Dictionary<string, Guid>>(version.PinnedChildVersionsJson ?? "{}")!;
        if (childPins.TryGetValue(currentActivity.NodeKey, out var pinnedChild)) config.VersionId = pinnedChild;
        if (currentActivity.ActivityType == ActivityType.MainActivity) config.WaitForCompletion = true;
        if (string.IsNullOrWhiteSpace(config.DefinitionKey))
            return Result.Failure(WorkflowErrors.Instance.CallActivityDefinitionMissing);

        var definition = await _definitionRepo.GetByKeyAsync(
            instance.OrganizationId, config.DefinitionKey, cancellationToken);
        if (definition is null || !definition.IsActive)
            return Result.Failure(WorkflowErrors.Definition.NotFound);

        var childVersion = config.VersionId.HasValue
            ? await _versionRepo.GetByIdWithProjectionAsync(config.VersionId.Value, cancellationToken)
            : await _versionRepo.GetLatestPublishedWithProjectionAsync(definition.Id, cancellationToken);
        var hasPinnedChild = version.PinnedChildVersionsJson is not null && JsonSerializer.Deserialize<Dictionary<string, Guid>>(version.PinnedChildVersionsJson)!.ContainsKey(currentActivity.NodeKey);
        if (childVersion is null || childVersion.WorkflowDefinitionId != definition.Id || childVersion.Status != WorkflowVersionStatus.Published && !(hasPinnedChild && childVersion.Status == WorkflowVersionStatus.Retired))
            return Result.Failure(WorkflowErrors.Instance.CallActivityNoPublishedVersion);

        var ancestor = instance;
        for (var depth = 0; ; depth++)
        {
            if (depth >= 15 || ancestor.PinnedWorkflowVersionId == childVersion.Id)
                return Result.Failure(new Error("Workflow.Call.Recursion", "Child workflows cannot recursively call an ancestor or exceed 16 levels."));
            if (ancestor.ParentInstanceId is not Guid ancestorId) break;
            var parent = await _instanceRepo.GetByIdAsync(ancestorId, cancellationToken);
            if (parent is null) break;
            ancestor = parent;
        }
        Dictionary<string, object?> childInputs;
        try { childInputs = IntegrationValueMapper.Map(JsonSerializer.Serialize(BuildInputVariables(await _variableRepo.GetByInstanceIdAsync(instance.Id, cancellationToken))), config.InputMappings); }
        catch (InvalidOperationException ex) { return Result.Failure(new Error("Workflow.Call.InputMapping", ex.Message)); }

        var createdCall = existingCall ?? await CreateActivityInstanceAsync(instance, currentActivity, now, cancellationToken);
        createdCall.AttachToken(activeToken?.Id);
        if (currentActivity.ActivityType == ActivityType.MainActivity)
        {
            createdCall.SetPhase("WaitingForChild");
            createdCall.SetDeadline(await ResolveUserTaskDueAtAsync(instance.OrganizationId, currentActivity.ConfigurationJson, createdCall.StartedAt, cancellationToken));
            if (_activityEvents is not null)
            {
                var entry = await _activityEvents.QueueAsync(instance, currentActivity, createdCall, "OnEnter", "entry", cancellationToken);
                if (entry.IsFailure) return Result.Failure(entry.Error);
                if (!entry.Value) { createdCall.SetPhase("WaitingForEnterEvents"); instance.AdvanceTo(currentActivity.NodeKey, now); return Result.Success(); }
            }
        }

        await _events.AppendAsync(
            instance.OrganizationId, instance.Id,
            WorkflowEventType.ActivityStarted, now, currentActivity.NodeKey,
            payloadJson: currentActivity.ConfigurationJson,
            cancellationToken: cancellationToken);

        if (currentActivity.ActivityType == ActivityType.MainActivity)
        {
            var hooks = await ExecuteHooksAsync(instance, currentActivity, createdCall, ActionExecutionTrigger.OnEnter, null, now, cancellationToken);
            if (hooks.IsFailure) return hooks;
        }

        var childIdempotency =
            $"call:{instance.Id}:{createdCall.Id}";

        var childResult = await StartCoreAsync(
            instance.OrganizationId,
            instance.WorkflowBindingId,
            instance.BusinessEntityId,
            childIdempotency,
            now,
            correlationId: instance.CorrelationId ?? instance.Id.ToString("N"),
            startedByUserId: instance.StartedByUserId,
            parentInstanceId: instance.Id,
            parentActivityNodeKey: config.WaitForCompletion ? currentActivity.NodeKey : null,
            pinnedWorkflowVersionId: childVersion.Id,
            cancellationToken: cancellationToken, initialVariables: childInputs, parentActivityInstanceId: createdCall.Id);

        if (childResult.IsFailure)
            return Result.Failure(childResult.Error);

        var activityInstances = await _activityRepo.GetByInstanceIdAsync(instance.Id, cancellationToken);
        var callAi = activityInstances.LastOrDefault(a =>
            a.ActivityNodeKey == currentActivity.NodeKey
            && a.ActivityType is ActivityType.CallActivity or ActivityType.MainActivity
            && a.Status == ActivityInstanceStatus.Active);

        if (config.WaitForCompletion)
        {
            if (childResult.Value.Status is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Failed or WorkflowInstanceStatus.Cancelled)
                return await ResumeFromCallActivityAsync(instance.Id, currentActivity.NodeKey, now, cancellationToken);
            // Child may have completed synchronously and already resumed the parent.
            if (callAi is null)
                return Result.Success();

            instance.AdvanceTo(currentActivity.NodeKey, now);
            return Result.Success(); // HALT — waiting for child End
        }

        // Fire-and-forget child: complete CallActivity and continue parent.
        if (callAi is null) return Result.Success(); // already resumed, including synchronous children
        callAi.Complete(now);

        var nextKey = GetSingleOutgoing(version, currentActivity.NodeKey);
        if (nextKey is null)
            return Result.Failure(WorkflowErrors.Instance.NoOutgoingTransition);

        instance.AdvanceTo(nextKey, now);
        return await AdvanceFromNodeAsync(instance, version, nextKey, now, cancellationToken, activeToken);
    }

    private static CallActivityConfig ParseCallActivityConfig(string? configurationJson)
        => IntegrationJson.Read<CallActivityConfig>(configurationJson);

    private sealed class CallActivityConfig
    {
        public string? DefinitionKey { get; set; }
        public Guid? VersionId { get; set; }
        public bool WaitForCompletion { get; set; } = true;
        public Dictionary<string, string> InputMappings { get; set; } = [];
        public Dictionary<string, string> OutputMappings { get; set; } = [];
    }
    private async Task<(Result Result, bool Halt, string? NextNodeKey)> ExecuteJoinGatewayAsync(
        WorkflowInstance instance,
        WorkflowVersion version,
        ActivityDefinition currentActivity,
        WorkflowExecutionToken? activeToken,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var tokens = await _tokenRepo.GetByInstanceIdAsync(instance.Id, cancellationToken);
        var joinTokens = tokens
            .Where(t => string.Equals(t.JoinNodeKey, currentActivity.NodeKey, StringComparison.Ordinal)
                && t.ForkActivityInstanceId == activeToken?.ForkActivityInstanceId)
            .ToList();

        if (activeToken?.JoinConsumed == true)
            return (Result.Success(), true, null);
        var tokenToComplete = activeToken ?? joinTokens.SingleOrDefault(t => t.Status == ExecutionTokenStatus.Active);
        if (tokenToComplete is not null && tokenToComplete.JoinNodeKey != currentActivity.NodeKey)
            return (Result.Failure(new Error("Workflow.Join.Mismatch", "A branch reached a different join than its configured fork.")), true, null);

        if (tokenToComplete is not null
            && tokenToComplete.Status == ExecutionTokenStatus.Active
            && (tokenToComplete.JoinNodeKey is null
                || string.Equals(tokenToComplete.JoinNodeKey, currentActivity.NodeKey, StringComparison.Ordinal)))
        {
            tokenToComplete.Complete(now);
            await _tokenRepo.SaveChangesAsync(cancellationToken);

            await _events.AppendAsync(
                instance.OrganizationId, instance.Id,
                WorkflowEventType.ParallelBranchCompleted, now, currentActivity.NodeKey,
                payloadJson: $"{{\"branchKey\":\"{tokenToComplete.BranchKey}\",\"tokenId\":\"{tokenToComplete.Id}\"}}",
                cancellationToken: cancellationToken);
        }

        tokens = await _tokenRepo.GetByInstanceIdAsync(instance.Id, cancellationToken);
        joinTokens = tokens
            .Where(t => string.Equals(t.JoinNodeKey, currentActivity.NodeKey, StringComparison.Ordinal)
                && t.ForkActivityInstanceId == activeToken?.ForkActivityInstanceId)
            .ToList();

        if (joinTokens.Count > 0 && joinTokens.Any(t => t.Status != ExecutionTokenStatus.Completed))
        {
            instance.AdvanceTo(currentActivity.NodeKey, now);
            return (Result.Success(), Halt: true, NextNodeKey: null);
        }

        var alreadyJoined = joinTokens.Any(t => t.JoinConsumed);
        if (alreadyJoined) return (Result.Success(), true, null);
        foreach (var joined in joinTokens) joined.ConsumeJoin();

        if (!alreadyJoined)
        {
            var activityInst = await CreateActivityInstanceAsync(instance, currentActivity, now, cancellationToken);
            activityInst.Complete(now);

            await _events.AppendAsync(
                instance.OrganizationId, instance.Id,
                WorkflowEventType.JoinCompleted, now, currentActivity.NodeKey,
                cancellationToken: cancellationToken);
        }

        var nextKey = GetSingleOutgoing(version, currentActivity.NodeKey);
        if (nextKey is null)
            return (Result.Failure(WorkflowErrors.Instance.NoOutgoingTransition), Halt: false, null);

        return (Result.Success(), Halt: false, nextKey);
    }

    private async Task DispatchOutcomeAsync(
        WorkflowInstance instance,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var binding = await _bindingRepo.GetByIdAsync(instance.WorkflowBindingId, cancellationToken);
        if (binding is null)
            return;

        if (binding.Mode is not (WorkflowBindingMode.Active or WorkflowBindingMode.Shadow))
            return;

        var variables = await _variableRepo.GetByInstanceIdAsync(instance.Id, cancellationToken);
        var lastAction = variables.FirstOrDefault(v =>
            string.Equals(v.VariableName, "LastActionTaken", StringComparison.OrdinalIgnoreCase));
        var outcomeVar = variables.FirstOrDefault(v =>
            string.Equals(v.VariableName, "OutcomeKey", StringComparison.OrdinalIgnoreCase));

        var outcomeKey = UnwrapJsonString(outcomeVar?.ValueJson)
                         ?? UnwrapJsonString(lastAction?.ValueJson)
                         ?? "Completed";

        var outputs = new Dictionary<string, object?>();
        foreach (var v in variables)
            outputs[v.VariableName] = v.ValueJson;

        if (binding.Mode == WorkflowBindingMode.Shadow)
            outputs["IsShadow"] = true;

        if (instance.StartedByUserId.HasValue)
            outputs["StartedByUserId"] = instance.StartedByUserId.Value;

        // Prefer the completing actor when present so Active-mode module callbacks can authorize.
        if (TryReadGuidOutput(outputs, "CompletedByUserId", out var completedBy))
            outputs["ActorUserId"] = completedBy;
        else if (TryReadGuidOutput(outputs, "ActorUserId", out var actor))
            outputs["ActorUserId"] = actor;
        else if (instance.StartedByUserId.HasValue)
            outputs["ActorUserId"] = instance.StartedByUserId.Value;

        var message = new WorkflowOutcomeMessage(
            instance.OrganizationId,
            binding.Id,
            instance.Id,
            binding.ModuleKey,
            binding.EntityType,
            instance.BusinessEntityId,
            outcomeKey,
            instance.CorrelationId ?? instance.Id.ToString("N"),
            outputs,
            now,
            MessageId: $"{instance.Id:N}:outcome:{outcomeKey}");

        var dispatchResult = await _outcomeDispatcher.DispatchAsync(message, cancellationToken);
        if (dispatchResult.IsFailure)
            throw new InvalidOperationException($"Workflow outcome dispatch failed: {dispatchResult.Error.Code}");

        await _events.AppendAsync(
            instance.OrganizationId, instance.Id,
            WorkflowEventType.OutcomePublished, now,
            payloadJson: $"{{\"outcomeKey\":\"{outcomeKey}\",\"mode\":\"{binding.Mode}\"}}",
            cancellationToken: cancellationToken);
    }

    private async Task<WorkflowVersion?> LoadPinnedVersionAsync(
        Guid versionId, CancellationToken cancellationToken)
    {
        // Global published versions are not ITenantAware. IgnoreQueryFilters is required so a
        // tenant-scoped runtime request can load the pinned definition version it started with
        // (SuperAdmin/global catalog, not a cross-tenant data leak).
        return await _db.WorkflowVersions
            .Include(v => v.Activities).ThenInclude(a => a.AssignmentRules)
            .Include(v => v.Transitions)
            .Include(v => v.Variables)
            .FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken);
    }

    private async Task<ActivityInstance> CreateActivityInstanceAsync(
        WorkflowInstance instance,
        ActivityDefinition activity,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var ai = ActivityInstance.Start(
            instance.OrganizationId, instance.Id,
            activity.NodeKey, activity.ActivityType, activity.Name, now);
        var snapshot = Workflow.Application.Workspace.WorkspaceDesign.Configuration(activity.ConfigurationJson)["publishedSla"];
        if (snapshot is not null)
        {
            var sla = snapshot.Deserialize<Workflow.Application.Workspace.PublishedSla>(IntegrationJson.Options)!;
            ai.SetDeadline(Workflow.Application.Workspace.PublishedSlaClock.Deadline(sla, now));
            ai.ScheduleSlaAlert(Workflow.Application.Workspace.PublishedSlaClock.Alerts(sla, now).FirstOrDefault()?.At);
        }
        await _activityRepo.AddAsync(ai, cancellationToken);
        return ai;
    }

    private async Task ProjectRequestAsync(
        WorkflowInstance instance,
        WorkflowBinding? binding,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (binding is not null)
            await _requestProjector.EnsureCreatedAsync(instance, binding, now, cancellationToken);

        var workItems = await _workItemRepo.GetByInstanceIdAsync(instance.Id, cancellationToken);
        var current = workItems.LastOrDefault(w =>
            w.Status is WorkItemStatus.Pending or WorkItemStatus.Claimed);

        string? nameEn = instance.CurrentActivityNodeKey;
        if (current is not null)
        {
            var activity = await _activityRepo.GetByIdAsync(current.ActivityInstanceId, cancellationToken);
            nameEn = activity?.Name ?? nameEn;
        }

        await _requestProjector.SyncCurrentTaskAsync(
            instance, current, nameEn, null, now, cancellationToken);
    }

    private static string? GetSingleOutgoing(WorkflowVersion version, string fromNodeKey)
    {
        var fromActivity = version.Activities.FirstOrDefault(a => a.NodeKey == fromNodeKey);
        if (fromActivity is null) return null;
        var transition = version.Transitions.FirstOrDefault(t => t.FromActivityDefinitionId == fromActivity.Id);
        if (transition is null) return null;
        return version.Activities.FirstOrDefault(a => a.Id == transition.ToActivityDefinitionId)?.NodeKey;
    }

    private static string? ResolveJoinNodeKey(WorkflowVersion version, ActivityDefinition parallelGateway)
    {
        if (!string.IsNullOrWhiteSpace(parallelGateway.ConfigurationJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(parallelGateway.ConfigurationJson);
                if (doc.RootElement.TryGetProperty("joinNodeKey", out var joinProp)
                    && joinProp.ValueKind == JsonValueKind.String)
                {
                    var key = joinProp.GetString();
                    if (!string.IsNullOrWhiteSpace(key))
                        return key;
                }
            }
            catch (JsonException)
            {
                // fall through
            }
        }

        return version.Activities
            .FirstOrDefault(a => a.ActivityType == ActivityType.JoinGateway)
            ?.NodeKey;
    }

    private static TimerConfig ParseTimerConfig(string? configurationJson)
    {
        var config = new TimerConfig(WorkflowTimerType.Duration, null, TimeSpan.FromHours(1), null);
        if (string.IsNullOrWhiteSpace(configurationJson))
            return config;

        try
        {
            using var doc = JsonDocument.Parse(configurationJson);
            var root = doc.RootElement;

            var timerType = WorkflowTimerType.Duration;
            if (root.TryGetProperty("timerType", out var typeProp)
                && typeProp.ValueKind == JsonValueKind.String
                && Enum.TryParse<WorkflowTimerType>(typeProp.GetString(), ignoreCase: true, out var parsed))
            {
                timerType = parsed;
            }

            DateTime? dueAt = null;
            if (root.TryGetProperty("dueAt", out var dueProp)
                && dueProp.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(dueProp.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var parsedDue))
            {
                dueAt = parsedDue.UtcDateTime;
            }

            TimeSpan? duration = null;
            if (root.TryGetProperty("duration", out var durProp)
                && durProp.ValueKind == JsonValueKind.String
                && TimeSpan.TryParse(durProp.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var parsedDur))
            {
                duration = parsedDur;
            }

            string? signalKey = null;
            if (root.TryGetProperty("signalKey", out var sigProp)
                && sigProp.ValueKind == JsonValueKind.String)
            {
                signalKey = sigProp.GetString();
            }

            return new TimerConfig(timerType, dueAt, duration ?? TimeSpan.FromHours(1), signalKey);
        }
        catch (JsonException)
        {
            return config;
        }
    }

    private static DateTime ResolveTimerDueAt(TimerConfig config, DateTime now)
    {
        if (config.TimerType == WorkflowTimerType.DueDate && config.DueAt.HasValue)
            return config.DueAt.Value;

        if (config.DueAt.HasValue)
            return config.DueAt.Value;

        return now.Add(config.Duration ?? TimeSpan.FromHours(1));
    }

    private static NotificationConfig ParseNotificationConfig(string? configurationJson)
    {
        var config = new NotificationConfig(
            "workflow.default",
            WorkflowNotificationChannel.InApp,
            NotificationFailurePolicy.Continue,
            Array.Empty<Guid>());

        if (string.IsNullOrWhiteSpace(configurationJson))
            return config;

        try
        {
            using var doc = JsonDocument.Parse(configurationJson);
            var root = doc.RootElement;

            var templateKey = config.TemplateKey;
            if (root.TryGetProperty("templateKey", out var tk) && tk.ValueKind == JsonValueKind.String)
                templateKey = tk.GetString() ?? templateKey;

            var channels = config.Channels;
            if (root.TryGetProperty("channels", out var ch) && ch.ValueKind == JsonValueKind.String
                && Enum.TryParse<WorkflowNotificationChannel>(ch.GetString(), ignoreCase: true, out var parsedCh))
            {
                channels = parsedCh;
            }

            var policy = NotificationFailurePolicy.Continue;
            if (root.TryGetProperty("failurePolicy", out var fp)
                && fp.ValueKind == JsonValueKind.String
                && Enum.TryParse<NotificationFailurePolicy>(fp.GetString(), ignoreCase: true, out var parsedPolicy))
            {
                policy = parsedPolicy;
            }
            else if (root.TryGetProperty("notificationFailurePolicy", out var nfp)
                     && nfp.ValueKind == JsonValueKind.String
                     && Enum.TryParse<NotificationFailurePolicy>(nfp.GetString(), ignoreCase: true, out var parsedNfp))
            {
                policy = parsedNfp;
            }

            var recipients = new List<Guid>();
            if (root.TryGetProperty("recipientUserIds", out var rids) && rids.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in rids.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && Guid.TryParse(item.GetString(), out var id))
                        recipients.Add(id);
                }
            }

            return new NotificationConfig(templateKey, channels, policy, recipients);
        }
        catch (JsonException)
        {
            return config;
        }
    }

    private static IReadOnlyDictionary<string, object?> BuildInputVariables(
        IReadOnlyList<WorkflowVariable> variables)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in variables)
            dict[v.VariableName] = DeserializeVariableValue(v.ValueJson);
        return dict;
    }

    private static object? DeserializeVariableValue(string? valueJson)
    {
        if (valueJson is null)
            return null;

        try
        {
            using var document = JsonDocument.Parse(valueJson);
            var value = document.RootElement;
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
                JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
                JsonValueKind.Null => null,
                _ => value.Clone(),
            };
        }
        catch (JsonException)
        {
            return valueJson;
        }
    }

    private static string? UnwrapJsonString(string? valueJson)
    {
        if (string.IsNullOrWhiteSpace(valueJson))
            return null;

        try
        {
            var raw = JsonSerializer.Deserialize<string>(valueJson);
            return string.IsNullOrWhiteSpace(raw) ? valueJson.Trim('"') : raw;
        }
        catch
        {
            return valueJson.Trim('"');
        }
    }

    private static bool TryReadGuidOutput(
        IReadOnlyDictionary<string, object?> outputs,
        string key,
        out Guid value)
    {
        value = Guid.Empty;
        if (!outputs.TryGetValue(key, out var raw) || raw is null)
            return false;

        if (raw is Guid g && g != Guid.Empty)
        {
            value = g;
            return true;
        }

        var text = raw as string ?? raw.ToString();
        text = UnwrapJsonString(text) ?? text;
        return Guid.TryParse(text, out value) && value != Guid.Empty;
    }

    private async Task UpsertVariableAsync(
        WorkflowInstance instance,
        string name,
        string valueJson,
        VariableDataType dataType,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existing = await _variableRepo.GetByNameAsync(instance.Id, name, cancellationToken);
        if (existing is not null)
        {
            existing.SetValue(valueJson, now);
        }
        else
        {
            var variable = WorkflowVariable.Create(
                instance.OrganizationId, instance.Id, name, dataType, valueJson, now);
            await _variableRepo.AddAsync(variable, cancellationToken);
        }

        await _events.AppendAsync(
            instance.OrganizationId, instance.Id,
            WorkflowEventType.VariableSet, now,
            payloadJson: $"{{\"name\":\"{name}\"}}",
            cancellationToken: cancellationToken);
    }

    private async Task RecordTransitionTakenAsync(
        WorkflowInstance instance,
        string fromNodeKey,
        string toNodeKey,
        WorkflowTransition transition,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await _events.AppendAsync(
            instance.OrganizationId, instance.Id,
            WorkflowEventType.TransitionTaken, now,
            $"{fromNodeKey}->{toNodeKey}",
            cancellationToken: cancellationToken);

        var ti = TransitionInstance.Append(
            instance.OrganizationId,
            instance.Id,
            fromNodeKey,
            toNodeKey,
            now,
            transition.TransitionKey,
            transition.ConditionExpression,
            transition.IsDefault);

        await _transitionInstanceRepo.AddAsync(ti, cancellationToken);
    }

    private async Task<DateTime?> ResolveUserTaskDueAtAsync(
        Guid organizationId,
        string? configurationJson,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(configurationJson);
            var root = doc.RootElement;

            SlaPolicy? policy = null;
            if (root.TryGetProperty("publishedSla", out var pinnedSla))
                return Workflow.Application.Workspace.PublishedSlaClock.Deadline(pinnedSla.Deserialize<Workflow.Application.Workspace.PublishedSla>(IntegrationJson.Options)!, now);
            if (root.TryGetProperty("slaPolicyId", out var idProp)
                && idProp.ValueKind == JsonValueKind.String
                && Guid.TryParse(idProp.GetString(), out var policyId))
            {
                policy = await _slaRepo.GetByIdAsync(policyId, cancellationToken);
            }
            else if (root.TryGetProperty("slaPolicyCode", out var codeProp)
                     && codeProp.ValueKind == JsonValueKind.String
                     && !string.IsNullOrWhiteSpace(codeProp.GetString()))
            {
                policy = await _slaRepo.GetByCodeAsync(codeProp.GetString()!, organizationId, cancellationToken);
            }

            if (policy is null || !policy.IsActive)
                return root.TryGetProperty("slaDurationHours", out var hours) && hours.TryGetDouble(out var duration) && duration > 0
                    ? now.AddHours(Math.Min(duration, 87600)) : null;

            return await _calendarService.CalculateDueAtAsync(
                policy.BusinessCalendarId,
                now,
                policy.Duration,
                policy.DurationUnit,
                cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<Result> ExecuteScriptTaskAsync(
        WorkflowInstance instance,
        WorkflowVersion version,
        ActivityDefinition activity,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var activityInst = await CreateActivityInstanceAsync(instance, activity, now, cancellationToken);

        await _events.AppendAsync(
            instance.OrganizationId, instance.Id,
            WorkflowEventType.ActivityStarted, now, activity.NodeKey,
            cancellationToken: cancellationToken);

        var allowedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in version.Variables)
        {
            allowedNames.Add(def.VariableKey);
            if (!string.IsNullOrWhiteSpace(def.Name))
                allowedNames.Add(def.Name);
        }

        var existingVars = await _variableRepo.GetByInstanceIdAsync(instance.Id, cancellationToken);
        foreach (var v in existingVars)
            allowedNames.Add(v.VariableName);

        var assignments = ParseScriptAssignments(activity.ConfigurationJson);
        foreach (var (name, rawValue) in assignments)
        {
            if (!allowedNames.Contains(name))
                return Result.Failure(new Error("Workflow.Variable.Unknown", $"Define variable '{name}' before assigning it."));

            var valueJson = ResolveScriptValue(rawValue, existingVars);
            using var valueDocument = JsonDocument.Parse(valueJson);
            var valueType = version.Variables.FirstOrDefault(v => v.VariableKey == name)?.DataType
                ?? (valueDocument.RootElement.ValueKind switch { JsonValueKind.Number => VariableDataType.Decimal,
                    JsonValueKind.True or JsonValueKind.False => VariableDataType.Boolean, JsonValueKind.String => VariableDataType.String, _ => VariableDataType.Json });
            await UpsertVariableAsync(
                instance, name, valueJson, valueType, now, cancellationToken);

            // refresh local map for subsequent copies
            var refreshed = await _variableRepo.GetByNameAsync(instance.Id, name, cancellationToken);
            if (refreshed is not null)
            {
                var list = existingVars.ToList();
                var idx = list.FindIndex(x => string.Equals(x.VariableName, name, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) list[idx] = refreshed;
                else list.Add(refreshed);
                existingVars = list;
            }
        }

        activityInst.Complete(now);

        await _events.AppendAsync(
            instance.OrganizationId, instance.Id,
            WorkflowEventType.ActivityCompleted, now, activity.NodeKey,
            cancellationToken: cancellationToken);

        return Result.Success();
    }

    private static List<(string Name, string RawValue)> ParseScriptAssignments(string? configurationJson)
    {
        var result = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(configurationJson))
            return result;

        try
        {
            using var doc = JsonDocument.Parse(configurationJson);
            var root = doc.RootElement;

            // The designer writes an object; retain the legacy array representation.
            var typed = root.TryGetProperty("assignmentFormat", out var format) && format.ValueKind == JsonValueKind.String && format.GetString() == "typed";
            if (root.TryGetProperty("setVariables", out var variableObject)
                && variableObject.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in variableObject.EnumerateObject())
                {
                    var value = !typed && prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString() ?? string.Empty
                        : prop.Value.GetRawText();
                    result.Add((prop.Name, value));
                }
            }

            if (root.TryGetProperty("setVariables", out var setVars) && setVars.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in setVars.EnumerateArray())
                {
                    var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var value = item.TryGetProperty("value", out var v)
                        ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText())
                        : null;
                    if (!string.IsNullOrWhiteSpace(name) && value is not null)
                        result.Add((name, value));
                }
            }

            if (root.TryGetProperty("assignments", out var assignments) && assignments.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in assignments.EnumerateObject())
                {
                    var value = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString() ?? string.Empty
                        : prop.Value.GetRawText();
                    result.Add((prop.Name, value));
                }
            }
        }
        catch (JsonException)
        {
            // ignore invalid script config
        }

        return result;
    }

    /// <summary>
    /// SAFE script values: JSON literals, or simple copy from another variable name (no expressions/code).
    /// </summary>
    private static string ResolveScriptValue(string rawValue, IReadOnlyList<WorkflowVariable> existingVars)
    {
        var trimmed = rawValue.Trim();

        // Quoted literal
        if ((trimmed.StartsWith('"') && trimmed.EndsWith('"'))
            || (trimmed.StartsWith('\'') && trimmed.EndsWith('\'')))
        {
            var inner = trimmed.StartsWith('"') ? JsonSerializer.Deserialize<string>(trimmed) : trimmed[1..^1];
            return JsonSerializer.Serialize(inner);
        }

        // Boolean / number / null / json object-array literals
        if (trimmed is "true" or "false" or "null"
            || trimmed.StartsWith('{') || trimmed.StartsWith('[')
            || double.TryParse(trimmed, out _))
        {
            return trimmed;
        }

        // Simple variable copy: bare identifier matching an existing variable
        var source = existingVars.FirstOrDefault(v =>
            string.Equals(v.VariableName, trimmed, StringComparison.OrdinalIgnoreCase));
        if (source?.ValueJson is not null)
            return source.ValueJson;

        // Treat as string literal
        return JsonSerializer.Serialize(trimmed);
    }

    private static string? TryReadSignalKey(string configurationJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(configurationJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            // eventKey is the designer contract; signalKey is retained for legacy XML.
            if (doc.RootElement.TryGetProperty("eventKey", out var eventKey))
                return eventKey.ValueKind == JsonValueKind.String ? eventKey.GetString() : null;
            if (doc.RootElement.TryGetProperty("signalKey", out var sk)
                && sk.ValueKind == JsonValueKind.String)
            {
                return sk.GetString();
            }
        }
        catch (JsonException)
        {
            // ignore
        }

        return null;
    }

    private sealed record TimerConfig(
        WorkflowTimerType TimerType,
        DateTime? DueAt,
        TimeSpan? Duration,
        string? SignalKey);

    private sealed record NotificationConfig(
        string TemplateKey,
        WorkflowNotificationChannel Channels,
        NotificationFailurePolicy FailurePolicy,
        IReadOnlyList<Guid> RecipientUserIds);
    private async Task<Result> CreateApprovalTaskAsync(WorkflowInstance instance, ActivityDefinition currentActivity,
        ActivityInstance activityInst, DateTime now, CancellationToken cancellationToken)
    {
        if (await _db.WorkItems.AnyAsync(w => w.ActivityInstanceId == activityInst.Id, cancellationToken)) return Result.Success();
        var assignmentValues = (await _variableRepo.GetByInstanceIdAsync(instance.Id, cancellationToken))
            .ToDictionary(v => v.VariableName, v => v.ValueJson, StringComparer.OrdinalIgnoreCase);
        var rules = currentActivity.AssignmentRules.Where(r => r.IsActive)
            .OrderBy(r => r.IsFallback).ThenBy(r => r.Priority).ToList();
        var rule = rules.FirstOrDefault(r => string.IsNullOrWhiteSpace(r.Expression)
            || Workflow.Application.Helpers.WorkflowCondition.Evaluate(r.Expression, assignmentValues));
        Guid? groupId = rule?.ReferenceId;
        var assignmentKey = rule?.AssignmentKey;

        var groupResult = await _assignmentResolver.ResolveGroupAsync(
            instance.OrganizationId, instance.WorkflowBindingId,
            assignmentKey, groupId, cancellationToken);
        if (groupResult.IsFailure)
        {
            var fallback = IntegrationJson.Read<Workflow.Application.Helpers.WorkflowTaskConfiguration>(currentActivity.ConfigurationJson).FallbackAssignmentKey;
            if (!string.IsNullOrWhiteSpace(fallback)) groupResult = await _assignmentResolver.ResolveGroupAsync(
                instance.OrganizationId, instance.WorkflowBindingId, fallback, null, cancellationToken);
        }
        if (groupResult.IsFailure) return Result.Failure(groupResult.Error);

        var dueAt = await ResolveUserTaskDueAtAsync(
            instance.OrganizationId, currentActivity.ConfigurationJson, activityInst.StartedAt, cancellationToken);
        activityInst.SetDeadline(dueAt);

        var workItem = WorkItem.Create(
            instance.OrganizationId, instance.Id, activityInst.Id,
            groupResult.Value, now, dueAt);
        await _workItemRepo.AddAsync(workItem, cancellationToken);

        var candidates = await _candidateFactory.CreateCandidatesAsync(
            instance.OrganizationId, workItem.Id, groupResult.Value, now, cancellationToken);
        await _candidateRepo.AddRangeAsync(candidates, cancellationToken);

        await _events.AppendAsync(
            instance.OrganizationId, instance.Id,
            WorkflowEventType.ActivityStarted, now, currentActivity.NodeKey,
            cancellationToken: cancellationToken);

        await _events.AppendAsync(
            instance.OrganizationId, instance.Id,
            WorkflowEventType.CandidateAssigned, now, currentActivity.NodeKey,
            payloadJson: $"{{\"workItemId\":\"{workItem.Id}\",\"candidateCount\":{candidates.Count}}}",
            cancellationToken: cancellationToken);


        activityInst.SetPhase("AwaitingApproval");
        instance.AdvanceTo(currentActivity.NodeKey, now);
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

}
