namespace Auth.Domain.Options;

public sealed class ApiSecurityOptions
{
    public const string SectionName = "ApiSecurity";

    public bool EnableIpWhitelist { get; set; }
    public bool EnableXForwardHeader { get; set; }
    public RateLimitOptions ExternalApiRateLimit { get; set; } = new();
    public List<string> IpWhitelist { get; set; } = ["127.0.0.1", "::1"];
}

public sealed class RateLimitOptions
{
    public int PermitLimit { get; set; } = 20;
    public int WindowInMinutes { get; set; } = 5;
    public int QueueLimit { get; set; }
}
