using NWFM.Shared.Domain;
using NWFM.Shared.MultiTenancy;

namespace Workflow.Domain.Entities;

/// <summary>A durable execution receipt. Callable invocations never own an activity or token.</summary>
public sealed class WorkflowMappingInvocation : Entity, ITenantAware
{
    public Guid OrganizationId { get; private set; }
    public Guid RootInstanceId { get; private set; }
    public Guid OriginActivityId { get; private set; }
    public Guid? OriginTokenId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public Guid TargetInstanceId { get; private set; }
    public Guid TargetVersionId { get; private set; }
    public string TargetNodeKey { get; private set; } = "";
    public string ExecutionKind { get; private set; } = "Graph";
    public Guid? ParentInvocationId { get; private set; }
    public string ActionId { get; private set; } = "";
    public string OperationKey { get; private set; } = "";
    public string ConfigurationJson { get; private set; } = "{}";
    public string? InvocationInputsJson { get; private set; }
    public string Status { get; private set; } = "Preparing";
    public DateTime WindowAnchor { get; private set; }
    public DateTime? Deadline { get; private set; }
    public string WaitDependenciesJson { get; private set; } = "[]";
    public string CriteriaJson { get; private set; } = "[]";
    public string SelectionsJson { get; private set; } = "{}";
    public string SourcesJson { get; private set; } = "[]";
    public string? PreparedRequestJson { get; private set; }
    public string? ResponseJson { get; private set; }
    public string? OutputsJson { get; private set; }
    public string? StepValuesJson { get; private set; }
    public Guid? FormVisitId { get; private set; }
    public bool Applied { get; private set; }
    public bool GraphAdvanced { get; private set; }
    public string? TimeoutOutcome { get; private set; }
    public string? Error { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private WorkflowMappingInvocation() { }
    public static WorkflowMappingInvocation Create(Guid organizationId, Guid rootId, Guid originActivityId,
        Guid? originTokenId, Guid actorId, Guid targetInstanceId, Guid targetVersionId, string nodeKey,
        string kind, string configuration, string operationKey, DateTime now, Guid? parentId = null,
        string actionId = "", string? inputs = null, Guid? formVisitId = null) => new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            RootInstanceId = rootId,
            OriginActivityId = originActivityId,
            OriginTokenId = originTokenId,
            ActorUserId = actorId,
            TargetInstanceId = targetInstanceId,
            TargetVersionId = targetVersionId,
            TargetNodeKey = nodeKey,
            ExecutionKind = kind,
            ConfigurationJson = configuration,
            OperationKey = operationKey,
            CreatedAt = now,
            WindowAnchor = now,
            ParentInvocationId = parentId,
            ActionId = actionId,
            InvocationInputsJson = inputs,
            FormVisitId = formVisitId
        };
    public void Bind(string criteria, string selections, string sources)
        => (CriteriaJson, SelectionsJson, SourcesJson) = (criteria, selections, sources);
    public void BindTarget(Guid instanceId) => TargetInstanceId = instanceId;
    public void Wait(DateTime deadline, string reason, string? outcome = null)
    {
        if (Deadline is null || deadline < Deadline) { Deadline = deadline; TimeoutOutcome = outcome; }
        Status = "Waiting"; Error = reason; Touch();
    }
    public void Touch() => SetUpdated(DateTime.UtcNow);
    public void ReplaceWaits(IReadOnlyList<WorkflowMappingWait> waits)
    {
        WaitDependenciesJson = System.Text.Json.JsonSerializer.Serialize(waits);
        var next = waits.OrderBy(w => w.Deadline).ThenBy(w => w.Key, StringComparer.Ordinal).FirstOrDefault();
        Deadline = next?.Deadline; TimeoutOutcome = next?.Outcome;
        if (next is not null) { Status = "Waiting"; Error = next.Reason; Touch(); }
        else if (Status == "Waiting") { Status = "Preparing"; Error = null; }
    }
    public void MarkGraphAdvanced() => GraphAdvanced = true;
    public void Attention(string reason) => (Status, Error) = ("Attention", reason);
    public void DeferRecovery(string reason) { Error = reason; Touch(); }
    public void Prepare(string request, string sources) => (PreparedRequestJson, SourcesJson, Status, Error) = (request, sources, "Prepared", null);
    public void RecordResponse(string response) => (ResponseJson, Status) = (response, "Delivered");
    public void Complete(string outputs, DateTime now) => (OutputsJson, Applied, Status, CompletedAt, Error) = (outputs, true, "Completed", now, null);
    public void PrepareStep(string values, string sources, DateTime now)
    {
        StepValuesJson = values; SourcesJson = sources; Complete("{}", now);
    }
    public void Cancel() => Status = "Cancelled";
    public void Retry() => (Status, Error) = (ResponseJson is null ? PreparedRequestJson is null ? "Preparing" : "Prepared" : "Delivered", null);
}
public sealed record WorkflowMappingWait(string Key, DateTime Anchor, DateTime Deadline, string Outcome, string Reason);
