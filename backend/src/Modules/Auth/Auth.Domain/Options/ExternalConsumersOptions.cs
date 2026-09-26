namespace Auth.Domain.Options;

public sealed class ExternalConsumersOptions
{
    public const string SectionName = "ExternalConsumers";

    public List<ExternalConsumerApp> WFM { get; set; } = [];
    public List<ExternalConsumerApp> C2M { get; set; } = [];
    public List<ExternalConsumerApp> Internal { get; set; } = [];
    public List<ExternalConsumerApp> Mob { get; set; } = [];

    public ExternalConsumerApp? FindByApiKey(string apiKey)
    {
        return All().FirstOrDefault(c =>
            string.Equals(c.ApiKey, apiKey, StringComparison.Ordinal));
    }

    public ExternalConsumerApp? FindByApiKey(string apiKey, string systemName)
    {
        return GetConsumers(systemName)
            .FirstOrDefault(c => string.Equals(c.ApiKey, apiKey, StringComparison.Ordinal));
    }

    private List<ExternalConsumerApp> GetConsumers(string systemName) =>
        systemName.ToUpperInvariant() switch
        {
            "WFM" => WFM,
            "C2M" => C2M,
            "INTERNAL" => Internal,
            "MOB" => Mob,
            _ => []
        };

    private IEnumerable<ExternalConsumerApp> All() =>
        WFM.Concat(C2M).Concat(Internal).Concat(Mob);
}

public sealed class ExternalConsumerApp
{
    public string AppID { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string SystemName { get; set; } = string.Empty;
}
