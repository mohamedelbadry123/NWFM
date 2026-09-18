namespace NWFM.Api.Services;
using NWFM.Shared.Integration.Workflow;

/// <summary>Standalone results are already durable in workflow history and the request projection.</summary>
public sealed class StandaloneOutcomeHandler(ILogger<StandaloneOutcomeHandler> logger) : IWorkflowOutcomeHandler
{
    public string ModuleKey => "Standalone";
    public Task HandleAsync(WorkflowOutcomeMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Standalone workflow {InstanceId} completed with {Outcome}", message.WorkflowInstanceId, message.OutcomeKey);
        return Task.CompletedTask;
    }
}
