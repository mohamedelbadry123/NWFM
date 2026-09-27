namespace Workflow.Infrastructure.Services;

using NWFM.Shared.Integration.Workflow;

/// <summary>Catalog marker. The engine dispatches this action through durable integration jobs.</summary>
internal sealed class HttpWorkflowActionProvider : IWorkflowActionProvider
{
    public IReadOnlyList<WorkflowActionDescriptor> GetDescriptors() => [new("http.request", "Call API", "استدعاء واجهة برمجية", "Standalone")];
    public Task<WorkflowActionExecutionResult> ExecuteAsync(WorkflowActionExecutionContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(WorkflowActionExecutionResult.Failed("Workflow.Http.RequiresDurableExecution", "HTTP actions must execute through the durable job worker."));
}
