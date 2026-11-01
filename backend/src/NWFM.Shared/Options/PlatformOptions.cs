namespace NWFM.Shared.Options;

/// <summary>SaaS platform settings: signup, provisioning, support access. Bound from <c>Platform</c>.</summary>
public sealed class PlatformOptions
{
    public const string SectionName = "Platform";

    /// <summary>Public landing / signup origin (the platform web app), used to build e-mail verification links.</summary>
    public string PublicAppBaseUrl { get; set; } = "http://localhost:4300";

    /// <summary>The staff console, where a tenant-side support session hands the user back to.</summary>
    public string ConsoleUrl { get; set; } = "http://localhost:4300/console";

    /// <summary>Tenant web origin; <c>{key}</c> is replaced with the tenant key.</summary>
    public string TenantAppUrlTemplate { get; set; } = "http://{key}.localhost:4200";

    /// <summary>Public IPs the API connects out from — what a bring-your-own database must allow.</summary>
    public string[] EgressIps { get; set; } = [];

    public string ManagedDatabaseNamePrefix { get; set; } = "nwfm_t_";

    public string DefaultPlanCode { get; set; } = "trial";

    public int SignupEmailTokenHours { get; set; } = 48;

    /// <summary>
    /// A self-service signup waits for staff approval after its e-mail is confirmed. Workspaces staff
    /// create from the console never wait.
    /// </summary>
    public bool RequireSignupApproval { get; set; } = true;

    /// <summary>Lifetime of the tenant administrator's set-password link.</summary>
    public int AdminActivationHours { get; set; } = 72;

    public int MaxSupportGrantHours { get; set; } = 72;

    public int SupportSessionMinutes { get; set; } = 60;

    /// <summary>How long the one-time support handoff code stays redeemable.</summary>
    public int SupportHandoffSeconds { get; set; } = 60;

    /// <summary>
    /// Allow a bring-your-own database on a private or loopback address. Off in production — on,
    /// the connection test becomes a way to probe the internal network.
    /// </summary>
    public bool AllowPrivateNetworkDatabaseHosts { get; set; }

    public CaptchaOptions Captcha { get; set; } = new();

    /// <summary>First platform administrator, created when the catalog has none.</summary>
    public BootstrapPlatformAdminOptions BootstrapAdmin { get; set; } = new();
}

public sealed class CaptchaOptions
{
    /// <summary><c>None</c> (development only) or <c>Turnstile</c>.</summary>
    public string Provider { get; set; } = CaptchaProviders.None;

    public string SiteKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string VerifyUrl { get; set; } = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
}

public static class CaptchaProviders
{
    public const string None = "None";
    public const string Turnstile = "Turnstile";
}

public sealed class BootstrapPlatformAdminOptions
{
    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string DisplayName { get; set; } = "Platform Administrator";
}
