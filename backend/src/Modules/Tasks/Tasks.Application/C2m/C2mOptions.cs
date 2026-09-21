namespace Tasks.Application.C2m;

/// <summary>
/// The C2M closure endpoint a finished field activity is reported to. Section <c>C2m</c>.
/// Ported from the reference app; off by default.
/// </summary>
public sealed class C2mOptions
{
    public const string SectionName = "C2m";

    /// <summary>
    /// When false, a closure is recorded as <c>SKIPPED</c> rather than sent, so the rest of the flow
    /// runs in an environment with no C2M to talk to.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Suppresses the close without switching the integration off — C2M's operators ask for this
    /// during a cutover, when they close the activity on their side. Still recorded as <c>SKIPPED</c>.
    /// </summary>
    public bool ByPassClosingInCcb { get; init; }

    /// <summary>Scheme and host only; the path is C2M's (<see cref="CloseFieldActivityPath"/>).</summary>
    public string? BaseUrl { get; init; }

    /// <summary>The path C2M already serves to WFM, kept verbatim.</summary>
    public string CloseFieldActivityPath { get; init; } = "/NwcCompass/MobilityCcbDirUpdateFARes/updateFAResponse";

    /// <summary>
    /// C2M authenticates this call with Basic auth. Never commit real values; set them through user
    /// secrets or environment variables (<c>C2m__Username</c>, <c>C2m__Password</c>).
    /// </summary>
    public string? Username { get; init; }

    public string? Password { get; init; }

    /// <summary>The operator account C2M attributes the closure to.</summary>
    public string UserId { get; init; } = "SPNWCALLDP";

    /// <summary>Identifies the calling system to C2M.</summary>
    public string SourceApp { get; init; } = "WFM";

    /// <summary>Where C2M can view the task's photos. <c>{faId}</c> is substituted; only the address travels.</summary>
    public string? ImageUrlTemplate { get; init; }

    /// <summary>Bounds a call nobody is waiting on — the background sender's.</summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// When true — the default — approving a task waits for C2M to accept the closure and fails the
    /// approval if it does not: the activity is C2M's, so a task C2M has not settled is not finished.
    /// When false, the approval commits first and the background sender pushes the closure.
    /// </summary>
    public bool WaitForAcknowledgement { get; init; } = true;

    /// <summary>Caps how long an approval waits on C2M; shorter than <see cref="TimeoutSeconds"/>, a reviewer is watching.</summary>
    public int AcknowledgementTimeoutSeconds { get; init; } = 15;

    /// <summary>How often the background sender looks for queued closures, and the least time between two tries of one.</summary>
    public int RetryIntervalSeconds { get; init; } = 300;

    /// <summary>Tries before a closure C2M never answered is given up as <c>FAILED</c>. A person can still send it again.</summary>
    public int MaxAttempts { get; init; } = 5;
}
