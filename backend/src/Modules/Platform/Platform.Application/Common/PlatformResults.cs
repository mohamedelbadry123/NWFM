namespace Platform.Application.Common;

/// <summary>
/// Builds a failed <see cref="Result{T}"/> from a message, an error code and the HTTP status the
/// control plane answers with — the shape every platform slice reports its refusals in.
/// </summary>
public static class PlatformResults
{
    public static Result<T> Fail<T>(string message, string code, int? httpStatusCode = null) =>
        Result.Failure<T>(new Error(code, message, httpStatusCode));
}

/// <summary>Error codes the control plane answers with.</summary>
public static class ApiErrorCodes
{
    public const string ValidationError = "Platform.Validation";
    public const string AuthenticationFailed = "Platform.AuthenticationFailed";
    public const string UnauthorizedAccess = "Platform.Forbidden";
    public const string NotFound = "Platform.NotFound";
    public const string InternalServiceError = "Server.Error";
    public const string ServiceUnavailable = "Server.Unavailable";
    public const string TooManyRequests = "Request.TooManyRequests";
    public const string BackendBusinessRuleValidation = "Platform.RuleViolated";

    public const string TenantNotFound = TenancyErrorCodes.TenantNotFound;
    public const string TenantSuspended = TenancyErrorCodes.TenantSuspended;
    public const string SupportAccessNotGranted = TenancyErrorCodes.SupportAccessNotGranted;

    public const string TenantKeyUnavailable = "Signup.TenantKeyUnavailable";
    public const string SignupInvalidState = "Signup.InvalidState";
    public const string ByoDatabaseCheckFailed = "Signup.DatabaseCheckFailed";
    public const string CaptchaFailed = "Signup.CaptchaFailed";
}
