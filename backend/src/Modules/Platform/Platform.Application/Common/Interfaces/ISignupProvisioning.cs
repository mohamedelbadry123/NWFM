namespace Platform.Application.Common.Interfaces;

/// <summary>
/// Turns a confirmed signup into a working tenant: reserve, database, migrate, seed, administrator,
/// activate, e-mail. Every step is checkpointed on the signup, so a re-run resumes after the last
/// step that finished. Never throws for a provisioning failure — it is recorded on the signup.
/// </summary>
public interface ISignupProvisioner
{
    Task ProvisionAsync(Guid signupRequestId, CancellationToken cancellationToken);
}

/// <summary>Runs <see cref="ISignupProvisioner"/> out of band: Hangfire when it is on, a background task when not.</summary>
public interface ISignupProvisioningQueue
{
    void Enqueue(Guid signupRequestId);
}

public interface ICaptchaVerifier
{
    /// <summary>Provider name and public site key the signup page renders the widget with.</summary>
    (string Provider, string SiteKey) ClientSettings { get; }

    Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken);
}

/// <summary>The caller's IP, resolved the same way the rate limiter and IP allowlist resolve it.</summary>
public interface IClientIpAccessor
{
    string? ClientIp { get; }
}
