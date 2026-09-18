namespace Workflow.Infrastructure.Services;

using Microsoft.Extensions.Options;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.Settings;

internal sealed class WorkflowFeatureGate : IWorkflowFeatureGate
{
    private readonly WorkflowSettings _settings;

    public WorkflowFeatureGate(IOptions<WorkflowSettings> settings)
        => _settings = settings.Value;

    public Result EnsureEnabled()
        => _settings.IsEnabled
            ? Result.Success()
            : Result.Failure(WorkflowErrors.ModuleDisabled);
}
