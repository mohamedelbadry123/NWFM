using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Results;
using Workflow.Application.Integrations;
using Workflow.Application.Workspace;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

namespace Workflow.Infrastructure.Services;

internal sealed partial class WorkflowWorkspace
{
    public Task<Result<WorkspaceWorkflow>> OpenChildAsync(WorkspaceChildInput input, Guid actor, CancellationToken ct) =>
        WorkflowExecutionLock.RunAsync(db, "workspace-create:" + tenant.OrganizationId, async () =>
        {
            var parent = await versions.GetByIdWithProjectionAsync(input.ParentVersionId, ct);
            if (actor == Guid.Empty || parent is null || !await db.WorkflowDefinitions.AnyAsync(
                d => d.Id == parent.WorkflowDefinitionId && d.OrganizationId == tenant.OrganizationId && (!parent.IsDraft || d.IsActive), ct))
                return Result.Failure<WorkspaceWorkflow>(Invalid("Workflow not found."));
            var settings = WorkflowWorkspaceDefinition.Read(parent.WorkspaceJson);
            var node = parent.Activities.FirstOrDefault(a => a.NodeKey == input.NodeKey && a.ActivityType == ActivityType.MainActivity);
            if (settings is not { IsHierarchical: true, Kind: "Main" } || node is null)
                return Result.Failure<WorkspaceWorkflow>(Invalid("Select a Task Type in a main workflow."));
            var config = IntegrationJson.Read<BusinessActivityConfiguration>(node.ConfigurationJson);
            if (config.TaskTypeId is not Guid typeId || parent.IsDraft && (taskTypes is null || !await taskTypes.IsActiveAsync(typeId, ct)))
                return Result.Failure<WorkspaceWorkflow>(Invalid("Select an active Task Type before creating its child workflow."));

            var candidates = await (from v in db.WorkflowVersions.AsNoTracking()
                                    join d in db.WorkflowDefinitions on v.WorkflowDefinitionId equals d.Id
                                    where d.OrganizationId == tenant.OrganizationId && (d.IsActive || v.Id == config.VersionId) && v.WorkspaceJson != null
                                        && (v.Status == WorkflowVersionStatus.Draft || v.Status == WorkflowVersionStatus.Published || v.Id == config.VersionId)
                                    select new WorkspaceWorkflow(d.Id, d.Name, d.NameAr, v.Id, v.VersionNumber, v.WorkspaceJson, d.DefinitionKey)).ToListAsync(ct);
            var owned = candidates.Where(c => IsOwnedChild(c.WorkspaceJson, settings, parent.WorkflowDefinitionId, node.NodeKey, typeId)).ToList();
            // Opening a published parent must always inspect its pinned child, without creating anything.
            var existing = !parent.IsDraft ? owned.FirstOrDefault(c => c.VersionId == config.VersionId)
                : owned.OrderByDescending(c => c.VersionNumber).FirstOrDefault();
            if (existing is not null) return Result.Success(existing);
            if (!parent.IsDraft) return Result.Failure<WorkspaceWorkflow>(Invalid("The pinned child workflow is unavailable."));

            var childSettings = new WorkflowWorkspaceDefinition("Child", DesignerVersion: settings.DesignerVersion)
            {
                SchemaVersion = 2,
                OrganizationScopes = settings.OrganizationScopes,
                ScopeRows = settings.ScopeRows,
                TaskTypeId = typeId,
                ParentDefinitionId = parent.WorkflowDefinitionId,
                ParentNodeKey = node.NodeKey,
            };
            var created = await CreateDefinitionAsync(new(node.Name + " — steps", node.NameAr, childSettings), actor, true, ct);
            if (created.IsFailure) return Result.Failure<WorkspaceWorkflow>(created.Error);
            var definition = await db.WorkflowDefinitions.SingleAsync(d => d.Id == created.Value.DefinitionId, ct);
            return Result.Success(new WorkspaceWorkflow(definition.Id, definition.Name, definition.NameAr,
                created.Value.VersionId, 1, JsonSerializer.Serialize(childSettings, IntegrationJson.Options), definition.DefinitionKey));
        }, ct);

    private static bool IsOwnedChild(string? json, WorkflowWorkspaceDefinition parent, Guid definitionId, string nodeKey, Guid taskTypeId)
    {
        try
        {
            return WorkflowWorkspaceDefinition.Read(json) is { IsHierarchical: true, Kind: "Child" } child
                && child.ParentDefinitionId == definitionId && child.ParentNodeKey == nodeKey
                && child.TaskTypeId == taskTypeId && WorkflowChildMatch.SameScope(parent, child);
        }
        catch (JsonException) { return false; }
    }
}
