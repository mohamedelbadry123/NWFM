namespace Workflow.Application.Queries.GetModuleCatalog;

using MediatR;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;

public sealed class GetModuleCatalogQueryHandler
    : IRequestHandler<GetModuleCatalogQuery, Result<IReadOnlyList<ModuleCatalogEntry>>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IEnumerable<IWorkflowCapabilityProvider> _providers;

    public GetModuleCatalogQueryHandler(
        IWorkflowFeatureGate gate,
        IEnumerable<IWorkflowCapabilityProvider> providers)
    {
        _gate = gate;
        _providers = providers;
    }

    public Task<Result<IReadOnlyList<ModuleCatalogEntry>>> Handle(
        GetModuleCatalogQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure)
            return Task.FromResult(Result.Failure<IReadOnlyList<ModuleCatalogEntry>>(gateResult.Error));

        var staticEntries = ModuleCatalog.GetAll();
        var providerByKey = _providers
            .GroupBy(p => p.ModuleKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var merged = new List<ModuleCatalogEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in staticEntries)
        {
            if (providerByKey.TryGetValue(entry.ModuleKey, out var provider))
            {
                merged.Add(MapProvider(entry, provider));
                seen.Add(entry.ModuleKey);
            }
            else
            {
                merged.Add(entry);
                seen.Add(entry.ModuleKey);
            }
        }

        foreach (var provider in _providers)
        {
            if (seen.Contains(provider.ModuleKey))
                continue;

            merged.Add(MapProvider(
                new ModuleCatalogEntry(provider.ModuleKey, provider.ModuleKey, provider.ModuleKey, []),
                provider));
        }

        return Task.FromResult(Result.Success<IReadOnlyList<ModuleCatalogEntry>>(merged));
    }

    private static ModuleCatalogEntry MapProvider(
        ModuleCatalogEntry staticEntry,
        IWorkflowCapabilityProvider provider)
    {
        var entities = provider.GetEntities()
            .Select(e => new EntityTypeEntry(
                e.EntityType,
                e.NameEn,
                e.NameAr,
                e.Triggers.Select(t => new TriggerEventEntry(t.EventKey, t.NameEn, t.NameAr, t.ScreenKey)).ToList()))
            .ToList();

        return new ModuleCatalogEntry(
            provider.ModuleKey,
            staticEntry.NameEn,
            staticEntry.NameAr,
            entities);
    }
}
