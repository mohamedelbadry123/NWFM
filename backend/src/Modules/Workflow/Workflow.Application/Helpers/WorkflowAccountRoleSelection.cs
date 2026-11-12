namespace Workflow.Application.Helpers;

using NWFM.Shared.Integration.Workflow;
using Workflow.Application.Integrations;
using Workflow.Application.Workspace;
using Workflow.Domain.Entities;

internal static class WorkflowAccountRoleSelection
{
    public static async Task<bool?> IsReadyAsync(ActivityDefinition activity, IWorkflowRoleDirectory? directory, CancellationToken ct)
    {
        try
        {
            var ids = IntegrationJson.Read<BusinessActivityConfiguration>(activity.ConfigurationJson).AssignmentRoleIds;
            if (ids is null) return false;
            if (ids.Count == 0) return null; // No role assignment: use legacy group readiness.
            if (directory is null || ids.Count > 50 || ids.Distinct().Count() != ids.Count || activity.AssignmentRules.Any(r => r.IsActive)) return false;
            var available = await directory.ListAsync(ct);
            return ids.All(id => id != Guid.Empty && available.Any(r => r.Id == id));
        }
        catch (System.Text.Json.JsonException) { return false; }
    }
}
