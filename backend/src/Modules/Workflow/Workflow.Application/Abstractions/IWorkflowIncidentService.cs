namespace Workflow.Application.Abstractions;

using NWFM.Shared.Results;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

/// <summary>
/// Opens and resolves operational workflow incidents. Stub for ops console / engine hooks.
/// </summary>
public interface IWorkflowIncidentService
{
    Task<Result<WorkflowIncident>> OpenAsync(
        Guid organizationId,
        Guid workflowInstanceId,
        WorkflowIncidentType incidentType,
        WorkflowIncidentSeverity severity,
        string title,
        DateTime now,
        Guid? activityInstanceId = null,
        string? activityNodeKey = null,
        string? errorCode = null,
        string? errorMessage = null,
        CancellationToken cancellationToken = default);

    Task<Result> MarkInProgressAsync(
        Guid incidentId,
        DateTime now,
        CancellationToken cancellationToken = default);

    Task<Result> ResolveAsync(
        Guid incidentId,
        Guid resolvedByUserId,
        DateTime now,
        string? notes = null,
        CancellationToken cancellationToken = default);

    Task<Result> IgnoreAsync(
        Guid incidentId,
        Guid ignoredByUserId,
        DateTime now,
        CancellationToken cancellationToken = default);
}
