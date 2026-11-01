namespace NWFM.Shared.Results;

/// <param name="Code">Stable, dotted error code, e.g. <c>Auth.UserNotFound</c>.</param>
/// <param name="Message">Human-readable message shown to the caller.</param>
/// <param name="Status">HTTP status for the failure; when null it is derived from the code's shape.</param>
public sealed record Error(string Code, string Message, int? Status = null)
{
    public static readonly Error None = new(string.Empty, string.Empty);
    public static readonly Error NullValue = new("Error.NullValue", "A null value was provided.");

    public static Error Validation(string field, string message) =>
        new($"Validation.{field}", message);
}
