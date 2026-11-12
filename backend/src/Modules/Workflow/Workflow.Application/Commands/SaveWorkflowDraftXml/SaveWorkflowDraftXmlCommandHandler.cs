namespace Workflow.Application.Commands.SaveWorkflowDraftXml;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Commands.CreateWorkflowDraft;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

public sealed class SaveWorkflowDraftXmlCommandHandler
    : IRequestHandler<SaveWorkflowDraftXmlCommand, Result<WorkflowVersionDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowVersionRepository _versionRepo;
    private readonly IWorkflowXmlCompiler _compiler;
    private readonly IWorkflowDraftLock? _draftLock;
    private readonly Microsoft.Extensions.Options.IOptions<Settings.WorkflowSettings>? _settings;

    public SaveWorkflowDraftXmlCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkflowVersionRepository versionRepo,
        IWorkflowXmlCompiler compiler,
        Microsoft.Extensions.Options.IOptions<Settings.WorkflowSettings>? settings = null, IWorkflowDraftLock? draftLock = null)
    {
        _gate = gate;
        _versionRepo = versionRepo;
        _compiler = compiler;
        _settings = settings;
        _draftLock = draftLock;
    }

    public Task<Result<WorkflowVersionDto>> Handle(SaveWorkflowDraftXmlCommand request, CancellationToken cancellationToken) =>
        _draftLock is null ? SaveAsync(request, cancellationToken) : _draftLock.RunAsync(request.VersionId, () => SaveAsync(request, cancellationToken), cancellationToken);

    private async Task<Result<WorkflowVersionDto>> SaveAsync(
        SaveWorkflowDraftXmlCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowVersionDto>(gateResult.Error);

        var version = await _versionRepo.GetByIdAsync(
            request.VersionId, cancellationToken);
        if (version is null)
            return Result.Failure<WorkflowVersionDto>(WorkflowErrors.Version.NotFound);

        if (!version.IsDraft)
            return Result.Failure<WorkflowVersionDto>(WorkflowErrors.Version.NotDraft);

        if (Routing.RoutingDefinition.Enabled(version.WorkspaceJson) && request.ExpectedRevision != Routing.RoutingDraftRevision.Of(version))
            return Result.Failure<WorkflowVersionDto>(new Error("Workflow.Draft.Conflict", "A newer draft exists. Your local changes are retained; reload the server version before saving again."));

        string normalizedXml;
        try { normalizedXml = Workspace.WorkspaceDesign.NormalizeDraft(request.XmlContent); }
        catch (Exception ex) when (ex is System.Xml.XmlException or System.Text.Json.JsonException or InvalidOperationException)
        { return Result.Failure<WorkflowVersionDto>(new Error("Workflow.InvalidDraft", ex.Message)); }
        var compileResult = _compiler.Compile(normalizedXml, out var canonicalHash);
        if (compileResult.IsFailure)
            return Result.Failure<WorkflowVersionDto>(compileResult.Error);

        var doc = compileResult.Value!;
        if (ScopeRowProblem(doc.WorkspaceJson) is { } scopeRowProblem)
            return Result.Failure<WorkflowVersionDto>(new Error(WorkflowScopeRows.InvalidCode, scopeRowProblem));
        if (Routing.RoutingDefinition.Enabled(doc.WorkspaceJson))
        {
            if (!Routing.RoutingDefinition.Enabled(version.WorkspaceJson) && !string.IsNullOrEmpty(version.XmlContent) && !request.AllowRoutingUpgrade)
                return Result.Failure<WorkflowVersionDto>(new Error("Routing.UpgradeRequired", "Use the upgrade preview to convert this draft and retain its previous definition."));
            if (_settings?.Value.RoutingV4Enabled != true)
                return Result.Failure<WorkflowVersionDto>(new Error("Routing.Disabled", "Routing version 4 authoring is not enabled."));
            if (request.ExpectedRevision != Routing.RoutingDraftRevision.Of(version))
                return Result.Failure<WorkflowVersionDto>(new Error("Workflow.Draft.Conflict", "The draft has changed. Reload before saving."));
        }
        else if (Routing.RoutingDefinition.Enabled(version.WorkspaceJson))
            return Result.Failure<WorkflowVersionDto>(new Error("Routing.Downgrade", "A version 4 draft cannot silently revert to legacy routing."));
        var now = DateTime.UtcNow;

        version.UpdateXml(normalizedXml, canonicalHash, now);
        version.SetWorkspace(doc.WorkspaceJson);
        if (request.DesignerJson is not null)
            version.UpdateDesignerJson(request.DesignerJson, now);

        var projection = BuildProjection(version.Id, doc, now);

        await _versionRepo.ReplaceProjectionAsync(
            version.Id, projection.Activities, projection.Transitions, projection.Variables,
            projection.Rules, projection.Outcomes, projection.Actions, cancellationToken);

        await _versionRepo.SaveChangesAsync(cancellationToken);

        return Result.Success(CreateWorkflowDraftCommandHandler.MapToDto(version));
    }

    /// <summary>
    /// Scope entries a draft carries must expand to the canonical scopes it saves beside them. Settings that cannot be
    /// read at all are left to publish validation, as before; only a readable document with inconsistent entries is refused.
    /// </summary>
    private static string? ScopeRowProblem(string? workspaceJson)
    {
        try { return WorkflowScopeRows.Validate(WorkflowWorkspaceDefinition.Read(workspaceJson)); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    /// <summary>The designer and operator-import path use identical XML-to-domain projection rules.</summary>
    public static Models.WorkflowVersionProjection BuildProjection(Guid versionId, Models.WorkflowXmlDocument document, DateTime now)
    {
        var activities = BuildActivities(versionId, document, now);
        var index = activities.ToDictionary(x => x.NodeKey);
        return new(activities, BuildTransitions(versionId, document, index, now), BuildVariables(versionId, document, now),
            BuildRules(document, index, now), BuildOutcomes(versionId, document, index, now), BuildActions(versionId, document, index, now));
    }

    private static List<ActivityDefinition> BuildActivities(
        Guid versionId, Models.WorkflowXmlDocument doc, DateTime now)
    {
        var list = new List<ActivityDefinition>(doc.Activities.Count);
        foreach (var a in doc.Activities)
        {
            if (!Enum.TryParse<ActivityType>(a.ActivityTypeName, true, out var actType))
                actType = ActivityType.UserTask;

            list.Add(ActivityDefinition.Create(
                versionId, a.NodeKey, actType, a.Name, now,
                a.NameAr, a.ActionKey, a.ConfigurationJson, a.PositionX, a.PositionY));
        }
        return list;
    }

    private static List<WorkflowTransition> BuildTransitions(
        Guid versionId,
        Models.WorkflowXmlDocument doc,
        Dictionary<string, ActivityDefinition> index,
        DateTime now)
    {
        var list = new List<WorkflowTransition>(doc.Transitions.Count);
        foreach (var t in doc.Transitions)
        {
            if (!index.TryGetValue(t.FromNodeKey, out var from) ||
                !index.TryGetValue(t.ToNodeKey, out var to))
                continue;

            list.Add(WorkflowTransition.Create(
                versionId, from.Id, to.Id,
                t.Key, t.Priority, now, t.IsDefault, t.ConditionExpression));
        }
        return list;
    }

    private static List<WorkflowVariableDefinition> BuildVariables(
        Guid versionId, Models.WorkflowXmlDocument doc, DateTime now)
    {
        var list = new List<WorkflowVariableDefinition>(doc.Variables.Count);
        foreach (var v in doc.Variables)
        {
            if (!Enum.TryParse<VariableDataType>(v.DataTypeName == "Number" ? "Decimal" : v.DataTypeName, true, out var dataType))
                dataType = VariableDataType.String;

            list.Add(WorkflowVariableDefinition.Create(
                versionId, v.Key, v.Name, dataType, now,
                v.NameAr, v.IsRequired, v.IsSensitive, v.DefaultValue,
                v.Description, v.DescriptionAr));
        }
        return list;
    }

    private static List<ActivityAssignmentRule> BuildRules(
        Models.WorkflowXmlDocument doc,
        Dictionary<string, ActivityDefinition> index,
        DateTime now)
    {
        var list = new List<ActivityAssignmentRule>();
        foreach (var a in doc.Activities)
        {
            if (!index.TryGetValue(a.NodeKey, out var activity)) continue;
            foreach (var r in a.AssignmentRules)
            {
                if (!Enum.TryParse<AssigneeType>(r.AssigneeTypeName, true, out var assigneeType))
                    assigneeType = AssigneeType.User;

                Guid? refId = Guid.TryParse(r.ReferenceId, out var g) ? g : null;

                list.Add(ActivityAssignmentRule.Create(
                    activity.Id, assigneeType, r.Priority, r.IsFallback, now,
                    assignmentKey: r.AssignmentKey,
                    referenceId: refId,
                    expression: r.Expression,
                    assignmentPurpose: r.AssignmentPurpose));
            }
        }
        return list;
    }

    private static List<ActivityOutcomeDefinition> BuildOutcomes(
        Guid versionId,
        Models.WorkflowXmlDocument doc,
        Dictionary<string, ActivityDefinition> index,
        DateTime now)
    {
        var list = new List<ActivityOutcomeDefinition>();
        foreach (var a in doc.Activities)
        {
            if (!index.TryGetValue(a.NodeKey, out var activity)) continue;
            foreach (var o in a.Outcomes)
            {
                list.Add(ActivityOutcomeDefinition.Create(
                    versionId, activity.Id, o.OutcomeKey, o.Name, o.SortOrder, now,
                    o.NameAr, o.Description, o.DescriptionAr,
                    o.RequiresComment, o.RequiresAttachment, o.IsDefault, o.ResultValue));
            }
        }
        return list;
    }

    private static List<ActivityActionDefinition> BuildActions(
        Guid versionId,
        Models.WorkflowXmlDocument doc,
        Dictionary<string, ActivityDefinition> index,
        DateTime now)
    {
        var list = new List<ActivityActionDefinition>();
        foreach (var a in doc.Activities)
        {
            if (!index.TryGetValue(a.NodeKey, out var activity)) continue;
            foreach (var ac in a.Actions)
            {
                if (!Enum.TryParse<ActionExecutionTrigger>(ac.ExecutionTriggerName, true, out var trigger))
                    trigger = ActionExecutionTrigger.OnComplete;

                if (!Enum.TryParse<ActionFailurePolicy>(ac.FailurePolicyName, true, out var failurePolicy))
                    failurePolicy = ActionFailurePolicy.Continue;

                list.Add(ActivityActionDefinition.Create(
                    versionId, activity.Id, ac.ActionKey, trigger, ac.Sequence, now,
                    ac.OutcomeKey, ac.ConditionExpression,
                    ac.InputMappingJson, ac.OutputMappingJson,
                    failurePolicy, ac.RetryCount, ac.RetryDelaySeconds, ac.TimeoutSeconds));
            }
        }
        return list;
    }
}
