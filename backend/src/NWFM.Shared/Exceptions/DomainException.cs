namespace NWFM.Shared.Exceptions;

/// <summary>
/// A business rule refused the operation. Carries an optional error code and HTTP status so the
/// exception handler can answer with the same envelope a failed <c>Result</c> would.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }

    public DomainException(string message, Exception innerException) : base(message, innerException) { }

    public DomainException(string message, string code, int? statusCode = null) : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    /// <summary>Error code; null means the generic <c>Domain.RuleViolated</c>.</summary>
    public string? Code { get; }

    /// <summary>HTTP status; null means 400.</summary>
    public int? StatusCode { get; }
}
