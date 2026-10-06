using NWFM.Shared.Integration.Workflow;
using Workflow.Application.DTOs;
using Workflow.Domain.Entities;

namespace Workflow.Infrastructure.Services;

internal static class WorkflowClassificationValidator
{
    public static async Task<IReadOnlyList<WorkflowValidationIssueDto>> ValidateAsync(
        WorkflowWorkspaceDefinition settings, IWorkflowReferenceData? references, IWorkflowTaskTypeCatalog? taskTypes, CancellationToken ct)
    {
        if (!settings.HasScopeSettings) return [];
        if (settings.SchemaVersion != 2)
            return [new("WORKSPACE_SCHEMA", "Unsupported workflow settings version.")];
        if (settings.Kind == WorkflowWorkspaceDefinition.MainKind)
        {
            if (settings.TaskTypeId is not null)
                return [new("WORKSPACE_TYPE", "Task Types is only available for child workflows.")];
            var activity = references is null ? null : (await references.ListAsync("field-activity-types", null, ct))
                .FirstOrDefault(t => t.Id == settings.FieldActivityTypeId);
            if (activity is null || activity.ParentCode != settings.DepartmentCode || activity.Code != settings.FieldActivityCode)
                return [new("WORKSPACE_ACTIVITY_TYPE", "Select an active Activity Type from Field Activity Types.")];
        }
        else
        {
            if (settings.FieldActivityTypeId is not null || settings.DepartmentCode is not null || settings.FieldActivityCode is not null)
                return [new("WORKSPACE_TYPE", "Activity Type is only available for main workflows.")];
            if (settings.TaskTypeId is not Guid id || taskTypes is null || !await taskTypes.IsActiveAsync(id, ct))
                return [new("WORKSPACE_TASK_TYPE", "Select an active Task Type.")];
        }
        return [];
    }
}
