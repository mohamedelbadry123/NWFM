namespace Workflow.Infrastructure.Services;

using MediatR;
using Microsoft.EntityFrameworkCore;
using Workflow.Infrastructure.Persistence;
using Workflow.Application.Commands.CompleteWorkItem;
using Workflow.Application.Commands.ClaimWorkItem;
using Workflow.Application.Commands.ReleaseWorkItem;
using Workflow.Application.Commands.ReassignWorkItem;
using Workflow.Application.Commands.DelegateWorkItem;
using Workflow.Application.Commands.CancelWorkflowInstance;
using Workflow.Application.Commands.SuspendWorkflowInstance;
using Workflow.Application.Commands.ResumeWorkflowInstance;
using Workflow.Application.Commands.RetryFailedActivity;

/// <summary>Serialize the entire command, including validation reads and task state changes.</summary>
internal sealed class WorkflowRuntimeCommandLock<TRequest, TResponse>(WorkflowDbContext db) : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        Guid? workItemId = request switch
        { CompleteWorkItemCommand c => c.WorkItemId, ClaimWorkItemCommand c => c.WorkItemId, ReleaseWorkItemCommand c => c.WorkItemId,
          ReassignWorkItemCommand c => c.WorkItemId, DelegateWorkItemCommand c => c.WorkItemId, _ => null };
        Guid? instanceId = request switch
        { CancelWorkflowInstanceCommand c => c.InstanceId, SuspendWorkflowInstanceCommand c => c.InstanceId,
          ResumeWorkflowInstanceCommand c => c.InstanceId, RetryFailedActivityCommand c => c.InstanceId, _ => null };
        if (workItemId.HasValue) instanceId = await db.WorkItems.AsNoTracking().Where(w => w.Id == workItemId).Select(w => (Guid?)w.WorkflowInstanceId).FirstOrDefaultAsync(ct);
        if (!instanceId.HasValue) return await next();
        return await WorkflowExecutionLock.RunAsync(db, "instance:" + instanceId, async () =>
        {
            if (workItemId.HasValue && !await WorkflowTreeGuard.CanRunAsync(db, instanceId.Value, ct))
                throw new UnauthorizedAccessException("This workflow or one of its parents is suspended or no longer running.");
            if (request is CompleteWorkItemCommand && await db.WorkItems.AnyAsync(w => w.Id == workItemId
                && db.IntegrationJobs.Any(j => j.ActivityInstanceId == w.ActivityInstanceId && j.IsActivityEvent && j.Required && j.Status != "Completed"), ct))
                return (TResponse)(object)NWFM.Shared.Results.Result.Failure<Workflow.Application.DTOs.WorkItemDto>(
                    new NWFM.Shared.Results.Error("Workflow.Events.Pending", "Required activity events must finish before completing this task."));
            return await next();
        }, ct);
    }
}
