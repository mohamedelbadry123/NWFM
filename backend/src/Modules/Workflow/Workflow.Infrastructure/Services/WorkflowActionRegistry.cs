namespace Workflow.Infrastructure.Services;

using NWFM.Shared.Integration.Workflow;

/// <summary>
/// Aggregates all registered IWorkflowActionProvider instances and resolves by ActionKey.
/// </summary>
internal sealed class WorkflowActionRegistry : IWorkflowActionRegistry
{
    private readonly IReadOnlyDictionary<string, IWorkflowActionProvider> _byKey;
    private readonly IReadOnlyList<WorkflowActionDescriptor> _all;

    public WorkflowActionRegistry(IEnumerable<IWorkflowActionProvider> providers)
    {
        var providerList = providers.ToList();
        var map = new Dictionary<string, IWorkflowActionProvider>(StringComparer.OrdinalIgnoreCase);
        var descriptors = new List<WorkflowActionDescriptor>();

        foreach (var provider in providerList)
        {
            foreach (var descriptor in provider.GetDescriptors())
            {
                map[descriptor.ActionKey] = provider;
                descriptors.Add(descriptor);
            }
        }

        _byKey = map;
        _all = descriptors;
    }

    public IWorkflowActionProvider? Resolve(string actionKey)
        => _byKey.TryGetValue(actionKey, out var provider) ? provider : null;

    public IReadOnlyList<WorkflowActionDescriptor> GetAll() => _all;
}
