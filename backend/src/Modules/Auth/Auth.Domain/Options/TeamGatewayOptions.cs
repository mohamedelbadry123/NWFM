namespace Auth.Domain.Options;

public sealed class TeamGatewayOptions
{
    public const string SectionName = "Fsms:FieldTeam";

    public bool RequireGateway { get; set; }
    public List<string> GatewayPeers { get; set; } = [];
}
