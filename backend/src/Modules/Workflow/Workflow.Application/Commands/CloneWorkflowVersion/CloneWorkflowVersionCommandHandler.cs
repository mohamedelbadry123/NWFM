namespace Workflow.Application.Commands.CloneWorkflowVersion;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Commands.CreateWorkflowDraft;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

public sealed class CloneWorkflowVersionCommandHandler
    : IRequestHandler<CloneWorkflowVersionCommand, Result<WorkflowVersionDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowDefinitionRepository _definitionRepo;
    private readonly IWorkflowVersionRepository _versionRepo;

    public CloneWorkflowVersionCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkflowDefinitionRepository definitionRepo,
        IWorkflowVersionRepository versionRepo)
    {
        _gate = gate;
        _definitionRepo = definitionRepo;
        _versionRepo = versionRepo;
    }

    public async Task<Result<WorkflowVersionDto>> Handle(
        CloneWorkflowVersionCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowVersionDto>(gateResult.Error);

        var source = await _versionRepo.GetByIdWithProjectionAsync(
            request.SourceVersionId, cancellationToken);
        if (source is null)
            return Result.Failure<WorkflowVersionDto>(WorkflowErrors.Version.NotFound);

        var definition = await _definitionRepo.GetByIdAsync(
            source.WorkflowDefinitionId, cancellationToken);
        if (definition is null)
            return Result.Failure<WorkflowVersionDto>(WorkflowErrors.Definition.NotFound);

        if (!definition.IsActive)
            return Result.Failure<WorkflowVersionDto>(WorkflowErrors.Definition.InactiveCannotPublish);

        var hasDraft = await _versionRepo.HasDraftAsync(
            source.WorkflowDefinitionId, cancellationToken);
        if (hasDraft)
            return Result.Failure<WorkflowVersionDto>(WorkflowErrors.Version.DraftAlreadyExists);

        var nextNumber = await _versionRepo.GetNextVersionNumberAsync(
            source.WorkflowDefinitionId, cancellationToken);

        var now = DateTime.UtcNow;
        var draft = WorkflowVersion.CreateDraft(
            source.WorkflowDefinitionId,
            nextNumber,
            request.CreatedByUserId,
            now,
            request.ChangeSummary ?? $"Cloned from v{source.VersionNumber}");

        if (!string.IsNullOrEmpty(source.XmlContent))
            draft.UpdateXml(source.XmlContent, source.XmlHash ?? string.Empty, now);
            draft.SetWorkspace(source.WorkspaceJson);

        if (!string.IsNullOrEmpty(source.DesignerJson))
            draft.UpdateDesignerJson(source.DesignerJson, now);

        await _versionRepo.AddAsync(draft, cancellationToken);

        if (source.Activities.Count > 0)
        {
            var activities = CloneActivities(draft.Id, source, now);
            var activityIndex = activities
                .Zip(source.Activities, (newA, oldA) => (oldA.NodeKey, newA))
                .ToDictionary(x => x.NodeKey, x => x.newA);

            var transitions = CloneTransitions(draft.Id, source, activityIndex, now);
            var variables = CloneVariables(draft.Id, source, now);
            var rules = CloneRules(source, activityIndex, now);
            var outcomes = CloneOutcomes(draft.Id, source, activityIndex, now);
            var actions = CloneActions(draft.Id, source, activityIndex, now);

            await _versionRepo.ReplaceProjectionAsync(
                draft.Id, activities, transitions, variables, rules, outcomes, actions, cancellationToken);
        }

        await _versionRepo.SaveChangesAsync(cancellationToken);

        return Result.Success(CreateWorkflowDraftCommandHandler.MapToDto(draft));
    }

    private static List<ActivityDefinition> CloneActivities(
        Guid versionId, WorkflowVersion source, DateTime now)
    {
        return source.Activities.Select(a => ActivityDefinition.Create(
            versionId, a.NodeKey, a.ActivityType, a.Name, now,
            a.NameAr, a.ActionKey, a.ConfigurationJson, a.PositionX, a.PositionY)).ToList();
    }

    private static List<WorkflowTransition> CloneTransitions(
        Guid versionId,
        WorkflowVersion source,
        Dictionary<string, ActivityDefinition> index,
        DateTime now)
    {
        return source.Transitions
            .Where(t =>
                index.TryGetValue(source.Activities.First(a => a.Id == t.FromActivityDefinitionId).NodeKey, out _) &&
                index.TryGetValue(source.Activities.First(a => a.Id == t.ToActivityDefinitionId).NodeKey, out _))
            .Select(t =>
            {
                var fromKey = source.Activities.First(a => a.Id == t.FromActivityDefinitionId).NodeKey;
                var toKey = source.Activities.First(a => a.Id == t.ToActivityDefinitionId).NodeKey;
                return WorkflowTransition.Create(
                    versionId, index[fromKey].Id, index[toKey].Id,
                    t.TransitionKey, t.Priority, now, t.IsDefault, t.ConditionExpression);
            }).ToList();
    }

    private static List<WorkflowVariableDefinition> CloneVariables(
        Guid versionId, WorkflowVersion source, DateTime now)
    {
        return source.Variables.Select(v => WorkflowVariableDefinition.Create(
            versionId, v.VariableKey, v.Name, v.DataType, now,
            v.NameAr, v.IsRequired, v.IsSensitive, v.DefaultValue,
            v.Description, v.DescriptionAr)).ToList();
    }

    private static List<ActivityAssignmentRule> CloneRules(
        WorkflowVersion source,
        Dictionary<string, ActivityDefinition> index,
        DateTime now)
    {
        var rules = new List<ActivityAssignmentRule>();
        foreach (var a in source.Activities)
        {
            if (!index.TryGetValue(a.NodeKey, out var newActivity)) continue;
            foreach (var r in a.AssignmentRules)
            {
                rules.Add(ActivityAssignmentRule.Create(
                    newActivity.Id, r.AssigneeType, r.Priority, r.IsFallback, now,
                    assignmentKey: r.AssignmentKey,
                    referenceId: r.ReferenceId,
                    expression: r.Expression,
                    assignmentPurpose: r.AssignmentPurpose));
            }
        }
        return rules;
    }

    private static List<ActivityOutcomeDefinition> CloneOutcomes(
        Guid versionId,
        WorkflowVersion source,
        Dictionary<string, ActivityDefinition> index,
        DateTime now)
    {
        var list = new List<ActivityOutcomeDefinition>();
        foreach (var a in source.Activities)
        {
            if (!index.TryGetValue(a.NodeKey, out var newActivity)) continue;
            foreach (var o in a.Outcomes)
            {
                list.Add(ActivityOutcomeDefinition.Create(
                    versionId, newActivity.Id, o.OutcomeKey, o.Name, o.SortOrder, now,
                    o.NameAr, o.Description, o.DescriptionAr,
                    o.RequiresComment, o.RequiresAttachment, o.IsDefault, o.ResultValue));
            }
        }
        return list;
    }

    private static List<ActivityActionDefinition> CloneActions(
        Guid versionId,
        WorkflowVersion source,
        Dictionary<string, ActivityDefinition> index,
        DateTime now)
    {
        var list = new List<ActivityActionDefinition>();
        foreach (var a in source.Activities)
        {
            if (!index.TryGetValue(a.NodeKey, out var newActivity)) continue;
            foreach (var ac in a.Actions)
            {
                list.Add(ActivityActionDefinition.Create(
                    versionId, newActivity.Id, ac.ActionKey, ac.ExecutionTrigger, ac.Sequence, now,
                    ac.OutcomeKey, ac.ConditionExpression,
                    ac.InputMappingJson, ac.OutputMappingJson,
                    ac.FailurePolicy, ac.RetryCount, ac.RetryDelaySeconds, ac.TimeoutSeconds));
            }
        }
        return list;
    }
}
