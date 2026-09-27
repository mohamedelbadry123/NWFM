using System.Text.Json;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;
using Workflow.Domain.Entities;

namespace Workflow.Application.Workspace;

public sealed record WorkspaceDefinitionInput(string Name, string? NameAr, WorkflowWorkspaceDefinition Settings);
public sealed record WorkspaceCreated(Guid DefinitionId, Guid VersionId);
public sealed record WorkspaceStartInput(Guid RequestId, string? Reference, bool IsDemo = false);
public sealed record WorkspaceActionInput(Guid RequestId, string Action, string? Comment = null, Dictionary<string, JsonElement>? FormValues = null, Guid? DemoActorId = null);
public sealed record WorkspaceWorkflow(Guid Id, string Name, string? NameAr, Guid VersionId, int VersionNumber, string? WorkspaceJson, string? DefinitionKey = null);
public sealed record WorkspaceInstanceSummary(Guid Id, string Name, string Status, string? Reference, DateTime StartedAt, bool IsDemo, string? GeographyJson);
public sealed record WorkspaceTask(WorkItemDto Task, bool CanAct, string? DisabledReason, bool CompletionBlocked = false);
public sealed record WorkspaceActivity(Guid Id, string NodeKey, string Name, string Type, string Status, string? Phase,
    DateTime StartedAt, DateTime? CompletedAt, DateTime? DueAt, string? DepartmentCode, string? FieldActivityCode, IReadOnlyList<WorkspaceTask> Tasks, string? AssignedGroup = null, PublishedSla? Sla = null);
public sealed record WorkspaceExecution(Guid Id, Guid? ParentInstanceId, Guid? ParentActivityInstanceId, string Name, string Status, IReadOnlyList<WorkspaceActivity> Activities);
public sealed record WorkspaceHistory(Guid Id, Guid InstanceId, string Type, string? NodeKey, Guid? ActorId, string? ActorName, DateTime OccurredAt, string? PayloadJson);
public sealed record WorkspaceOperation(Guid Id, Guid InstanceId, Guid ActivityId, string? Name, string Kind, string? Trigger, bool Required, string Status, int Attempts, string? Error, int? StatusCode, string? ResponseJson, string? EventNodeKey = null);
public sealed record WorkspaceActor(Guid UserId, string Name);
public sealed record WorkspaceDetail(Guid Id, bool IsDemo, string? GeographyJson, IReadOnlyList<WorkspaceExecution> Tree,
    IReadOnlyList<WorkspaceHistory> History, IReadOnlyList<WorkspaceOperation> Operations, IReadOnlyList<WorkspaceActor> DemoActors);

public interface IWorkflowWorkspace
{
    Task<Result<WorkspaceCreated>> CreateAsync(WorkspaceDefinitionInput input, Guid actor, CancellationToken ct);
    Task<IReadOnlyList<WorkspaceWorkflow>> CatalogAsync(CancellationToken ct);
    Task<IReadOnlyList<WorkspaceWorkflow>> ChildrenAsync(CancellationToken ct);
    Task<Result<Guid>> StartAsync(Guid definitionId, WorkspaceStartInput input, Guid actor, bool administrator, CancellationToken ct);
    Task<IReadOnlyList<WorkspaceInstanceSummary>> ListAsync(string? search, CancellationToken ct);
    Task<Result<WorkspaceDetail>> GetAsync(Guid id, Guid actor, bool administrator, Guid? demoActorId, CancellationToken ct);
    Task<Result> ActAsync(Guid workItemId, WorkspaceActionInput input, Guid actor, bool administrator, CancellationToken ct);
}
