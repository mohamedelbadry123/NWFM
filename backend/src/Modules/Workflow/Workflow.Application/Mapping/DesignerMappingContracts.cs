using System.Text.Json;
using System.Text.Json.Serialization;
using NWFM.Shared.Results;

namespace Workflow.Application.Mapping;

/// <summary>Bounded schema subset. Manual objects default closed; AdditionalProperties opens them, optionally constrained by AdditionalPropertySchema.
/// Required belongs to a property, Nullable to its value. Undefined Default means absent.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MappingSchema(string Type, bool Required = false, bool Nullable = false,
    Dictionary<string, MappingSchema>? Properties = null, MappingSchema? Items = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] JsonElement Default = default,
    bool Inferred = false, bool AdditionalProperties = false, MappingSchema? AdditionalPropertySchema = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EndpointParameter(string Location, string Name, MappingSchema Schema);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EndpointDefinition(string Method, string Path, List<EndpointParameter> Parameters,
    MappingSchema? Body = null, MappingSchema? Response = null);

/// <summary>FieldKey is one literal field/output key, including any dots. JsonPointer traverses inside that value only.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SourceReference(Guid WorkflowDefinitionId, Guid WorkflowVersionId, string NodeKey,
    string Kind, string FieldKey, string JsonPointer = "", string? FormStepId = null, Guid? FormId = null,
    int? FormVersion = null, string? SelectorId = null);
public sealed record SourceDescriptor(SourceReference Reference, string Label, IReadOnlyList<string> Breadcrumbs,
    MappingSchema Schema, string Availability, bool Sensitive = false);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MissingDataPolicy(string Mode = "Attention", int? DeadlineSeconds = null,
    string? TimeoutOutcome = null, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] JsonElement Fallback = default);
/// <summary>Equality only: BusinessKey means WorkflowInstance.BusinessEntityId; CorrelationKey means CorrelationId.
/// Freeze all expression values and the UTC window anchor when invocation starts.
/// Latest is descending StartedAt then ascending immutable instance ID; Attention is the default on ambiguity.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RelatedInstanceSelector(string Id, Guid WorkflowDefinitionId, Guid WorkflowVersionId,
    string KeyKind, TransformNode KeyValue, TransformNode? StartedByUserId = null, string? Status = null,
    int? WindowSeconds = null, string MultipleMatches = "Attention", MissingDataPolicy? Missing = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TransformNode(string Op, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] JsonElement Value = default, SourceReference? Source = null,
    List<TransformNode>? Args = null, Dictionary<string, TransformNode>? Properties = null, string JsonPointer = "",
    MissingDataPolicy? Missing = null, MappingSchema? LiteralSchema = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InputBinding(string Location, string Name, TransformNode Value);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OutputDefinition(string Id, string Name, MappingSchema Schema, TransformNode Value, bool Sensitive = false);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StepFieldBinding(string Id, string FormStepId, Guid FormId, int FormVersion, string FieldKey,
    string Mode, MappingSchema Schema, TransformNode Value);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CallableTarget(Guid WorkflowDefinitionId, Guid WorkflowVersionId, string NodeKey);
public sealed record MappingTreePin(Guid WorkflowDefinitionId, Guid WorkflowVersionId);
public sealed record CallableTargetDescriptor(CallableTarget Target, string Label, IReadOnlyList<string> Breadcrumbs,
    MappingSchema Inputs, bool Callable);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FollowUpAction(string Id, CallableTarget Target, string Mode, TransformNode Inputs,
    MissingDataPolicy? UnavailableTarget = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DesignerMappingConfiguration(int ContractVersion, EndpointDefinition? Endpoint,
    List<InputBinding> Inputs, List<RelatedInstanceSelector> Selectors, List<OutputDefinition> Outputs,
    List<StepFieldBinding> StepBindings, List<FollowUpAction> Actions, bool Callable = false,
    MappingSchema? InvocationInputs = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MappingTreePin? TreeRoot = null);
public sealed record MappingSample(SourceReference Source, JsonElement Value);
/// <summary>Prepared values must bypass legacy template rendering; the transport still enforces connection origin/DNS/auth rules.</summary>
public sealed record PreparedMappingRequest(string Method, string Path, Dictionary<string, string> Query,
    Dictionary<string, string> Headers, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] JsonElement Body = default);
public sealed record MappingAuthoringRequest(CallableTarget Owner, DesignerMappingConfiguration Configuration);
public sealed record MappingPreviewRequest(DesignerMappingConfiguration Configuration, List<MappingSample> Samples, CallableTarget? Owner = null, string Stage = "Request");
public sealed record PreparedFollowUp(string ActionId, CallableTarget Target, string Mode, JsonElement Inputs, MissingDataPolicy? UnavailableTarget);
public sealed record MappingPreviewResult(PreparedMappingRequest? Request, IReadOnlyList<string> Errors,
    IReadOnlyDictionary<string, JsonElement>? Outputs = null, IReadOnlyList<PreparedFollowUp>? Actions = null,
    IReadOnlyDictionary<string, JsonElement>? StepValues = null);
public sealed record SchemaImportRequest(string Document, string Path, string Method);
public sealed record MappingCatalog(IReadOnlyList<SourceDescriptor> Sources, IReadOnlyList<CallableTargetDescriptor> Targets,
    IReadOnlyList<CallableTarget>? TreeNodes = null, IReadOnlyList<MappingDependency>? Dependencies = null,
    IReadOnlyList<string>? Diagnostics = null, IReadOnlyList<RelatedMappingTree>? RelatedTrees = null);
public sealed record RelatedMappingTree(MappingTreePin Root, IReadOnlyList<CallableTarget> Nodes);
public sealed record MappingDependency(CallableTarget From, CallableTarget To, string Kind);
public sealed record MappingDefinitionChoice(Guid DefinitionId, Guid VersionId, int VersionNumber, string Name);
public interface IDesignerMappingCatalog
{
    Task<Result<MappingCatalog>> GetAsync(Guid definitionId, Guid versionId, CancellationToken ct);
    Task<IReadOnlyList<MappingDefinitionChoice>> RelatedAsync(CancellationToken ct);
}

/// <summary>Stage2B implements this boundary using immutable submitted/completed snapshots. Never expose it to sample preview.
/// Authorization is checked again for each source, even if a catalog entry was authorized while designing.</summary>
public interface IMappingSourceResolver
{
    Task<ResolvedMappingSource> ResolveAsync(SourceReference source, MappingResolutionContext context, CancellationToken ct);
}
public sealed record MappingResolutionContext(Guid OrganizationId, Guid ActorUserId, Guid RootInstanceId,
    Guid InvocationId, DateTimeOffset WindowAnchor, IReadOnlyDictionary<string, FrozenRelatedSelection> Selections);
public sealed record FrozenRelatedSelection(Guid? InstanceId, JsonElement KeyValue, Guid? StartedByUserId,
    DateTimeOffset WindowAnchor, DateTimeOffset? Deadline, string State, DateTimeOffset? EligibleAt = null);
public sealed record ResolvedMappingSource(string State, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] JsonElement Value, Guid? InstanceId, Guid? SubmissionId,
    Guid? ResultId, Guid? InvocationId, DateTimeOffset? CompletedAt, SourceReference Source,
    Guid? ActivityInstanceId = null, Guid? FormVisitId = null, IReadOnlyList<ResolvedMappingSource>? Dependencies = null);
public sealed record FrozenMappingInput(PreparedMappingRequest Request, IReadOnlyList<ResolvedMappingSource> Provenance,
    string ConfigurationJson, string OperationKey);
public sealed record FrozenRelatedCriteria(string SelectorId, Guid WorkflowDefinitionId, Guid WorkflowVersionId,
    string KeyKind, string KeyValue, Guid? StartedByUserId, string? Status, DateTimeOffset WindowAnchor,
    DateTimeOffset? WindowStart, DateTimeOffset? Deadline, string MultipleMatches, string TimeoutOutcome);
