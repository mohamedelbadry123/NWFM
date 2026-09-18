namespace Workflow.Infrastructure.Services;

using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

internal sealed class WorkflowIncidentService : IWorkflowIncidentService
{
    private readonly IWorkflowIncidentRepository _incidents;

    public WorkflowIncidentService(IWorkflowIncidentRepository incidents) => _incidents = incidents;

    public async Task<Result<WorkflowIncident>> OpenAsync(
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
        CancellationToken cancellationToken = default)
    {
        var incident = WorkflowIncident.Create(
            organizationId,
            workflowInstanceId,
            incidentType,
            severity,
            title,
            now,
            activityInstanceId,
            activityNodeKey,
            errorCode,
            errorMessage);

        await _incidents.AddAsync(incident, cancellationToken);
        return Result.Success(incident);
    }

    public async Task<Result> MarkInProgressAsync(Guid incidentId, DateTime now, CancellationToken cancellationToken = default)
    {
        var incident = await _incidents.GetByIdAsync(incidentId, cancellationToken);
        if (incident is null)
            return Result.Failure(WorkflowErrors.Incident.NotFound);

        if (incident.Status is WorkflowIncidentStatus.Resolved)
            return Result.Failure(WorkflowErrors.Incident.AlreadyResolved);

        if (incident.Status is WorkflowIncidentStatus.Ignored)
            return Result.Failure(WorkflowErrors.Incident.AlreadyIgnored);

        incident.MarkInProgress(now);
        await _incidents.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ResolveAsync(
        Guid incidentId,
        Guid resolvedByUserId,
        DateTime now,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var incident = await _incidents.GetByIdAsync(incidentId, cancellationToken);
        if (incident is null)
            return Result.Failure(WorkflowErrors.Incident.NotFound);

        if (incident.Status is WorkflowIncidentStatus.Resolved)
            return Result.Failure(WorkflowErrors.Incident.AlreadyResolved);

        if (incident.Status is WorkflowIncidentStatus.Ignored)
            return Result.Failure(WorkflowErrors.Incident.AlreadyIgnored);

        if (incident.Status is not (WorkflowIncidentStatus.Open or WorkflowIncidentStatus.InProgress))
            return Result.Failure(WorkflowErrors.Incident.NotOpen);

        incident.Resolve(resolvedByUserId, now, notes);
        await _incidents.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> IgnoreAsync(
        Guid incidentId,
        Guid ignoredByUserId,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var incident = await _incidents.GetByIdAsync(incidentId, cancellationToken);
        if (incident is null)
            return Result.Failure(WorkflowErrors.Incident.NotFound);

        if (incident.Status is WorkflowIncidentStatus.Resolved)
            return Result.Failure(WorkflowErrors.Incident.AlreadyResolved);

        if (incident.Status is WorkflowIncidentStatus.Ignored)
            return Result.Failure(WorkflowErrors.Incident.AlreadyIgnored);

        incident.Ignore(ignoredByUserId, now);
        await _incidents.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
