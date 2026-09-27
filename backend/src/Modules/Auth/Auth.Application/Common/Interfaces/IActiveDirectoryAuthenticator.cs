namespace Auth.Application.Common.Interfaces;

public interface IActiveDirectoryAuthenticator
{
    bool IsEnabled { get; }
    Task<ActiveDirectoryAuthResult> ValidateCredentialsAsync(string userName, string password, CancellationToken ct);
}

public enum ActiveDirectoryAuthOutcome { Skipped, Valid, Invalid, Unavailable }

public sealed record ActiveDirectoryAuthResult(ActiveDirectoryAuthOutcome Outcome, string? Error = null)
{
    public static readonly ActiveDirectoryAuthResult Skipped = new(ActiveDirectoryAuthOutcome.Skipped);
    public static readonly ActiveDirectoryAuthResult Valid = new(ActiveDirectoryAuthOutcome.Valid);
    public static readonly ActiveDirectoryAuthResult Invalid = new(ActiveDirectoryAuthOutcome.Invalid);
    public static ActiveDirectoryAuthResult Unavailable(string error) => new(ActiveDirectoryAuthOutcome.Unavailable, error);
}
