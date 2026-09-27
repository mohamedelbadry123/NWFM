namespace Workflow.Application.Queries.GetWorkflowVersionById;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

public sealed class GetWorkflowVersionByIdQueryHandler
    : IRequestHandler<GetWorkflowVersionByIdQuery, Result<WorkflowVersionDetailDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowVersionRepository _versionRepo;

    public GetWorkflowVersionByIdQueryHandler(
        IWorkflowFeatureGate gate, IWorkflowVersionRepository versionRepo)
    {
        _gate = gate;
        _versionRepo = versionRepo;
    }

    public async Task<Result<WorkflowVersionDetailDto>> Handle(
        GetWorkflowVersionByIdQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowVersionDetailDto>(gateResult.Error);

        var version = await _versionRepo.GetByIdWithProjectionAsync(
            request.VersionId, cancellationToken);
        if (version is null)
            return Result.Failure<WorkflowVersionDetailDto>(WorkflowErrors.Version.NotFound);

        return Result.Success(MapToDetailDto(version));
    }

    internal static WorkflowVersionDetailDto MapToDetailDto(WorkflowVersion v) =>
        new(v.Id, v.WorkflowDefinitionId, v.VersionNumber,
            v.Status, v.SchemaVersion, v.DesignerJson,
            v.ValidationStatus, v.ValidationResultJson,
            v.ChangeSummary, v.CreatedByUserId, v.PublishedByUserId, v.PublishedAt,
            v.Activities.Select(MapActivity).ToList(),
            v.Transitions.Select(MapTransition).ToList(),
            v.Variables.Select(MapVariable).ToList(),
            v.CreatedAt, v.UpdatedAt, v.WorkspaceJson, v.PinnedChildVersionsJson);

    private static ActivityDefinitionDto MapActivity(ActivityDefinition a) =>
        new(a.Id, a.WorkflowVersionId, a.NodeKey, a.ActivityType, a.Name, a.NameAr,
            a.ActionKey, a.ConfigurationJson, a.PositionX, a.PositionY,
            a.AssignmentRules.Select(MapRule).ToList(),
            a.Outcomes.Select(MapOutcome).ToList(),
            a.Actions.Select(MapAction).ToList());

    internal static ActivityOutcomeDefinitionDto MapOutcome(ActivityOutcomeDefinition o) =>
        new(o.Id, o.ActivityDefinitionId, o.OutcomeKey, o.Name, o.NameAr,
            o.Description, o.DescriptionAr, o.SortOrder,
            o.RequiresComment, o.RequiresAttachment, o.IsDefault, o.IsActive, o.ResultValue);

    private static ActivityActionDefinitionDto MapAction(ActivityActionDefinition a) =>
        new(a.Id, a.ActivityDefinitionId, a.ActionKey, a.ExecutionTrigger,
            a.OutcomeKey, a.ConditionExpression, a.Sequence,
            a.InputMappingJson, a.OutputMappingJson, a.FailurePolicy,
            a.RetryCount, a.RetryDelaySeconds, a.TimeoutSeconds, a.IsActive);

    private static ActivityAssignmentRuleDto MapRule(ActivityAssignmentRule r) =>
        new(r.Id, r.ActivityDefinitionId, r.AssigneeType, r.AssignmentKey,
            r.ReferenceId, r.Expression, r.Priority, r.IsFallback, r.IsActive, r.AssignmentPurpose);

    private static WorkflowTransitionDto MapTransition(WorkflowTransition t) =>
        new(t.Id, t.WorkflowVersionId, t.FromActivityDefinitionId, t.ToActivityDefinitionId,
            t.TransitionKey, t.ConditionExpression, t.IsDefault, t.Priority);

    private static WorkflowVariableDefinitionDto MapVariable(WorkflowVariableDefinition v) =>
        new(v.Id, v.WorkflowVersionId, v.VariableKey, v.Name, v.NameAr,
            v.DataType, v.IsRequired, v.IsSensitive, v.DefaultValue,
            v.Description, v.DescriptionAr);
}
