using NWFM.Shared.Results;

namespace Auth.Application.Constants;

public static class AuthErrors
{
    public static readonly Error InvalidCredentials = new("Auth.InvalidCredentials", "Invalid username or password.");
    public static readonly Error LockedOut = new("Auth.LockedOut", "Account is locked.");
    public static readonly Error NotFound = new("Auth.NotFound", "User not found.");
    public static readonly Error ValidationError = new("Auth.Validation", "Validation failed.");
    public static readonly Error Conflict = new("Auth.Conflict", "A user with this name already exists.");
    public static readonly Error OtpExpired = new("Auth.OtpExpired", "The OTP challenge has expired.");
    public static readonly Error OtpInvalid = new("Auth.OtpInvalid", "The OTP code is invalid.");
    public static readonly Error OtpMaxAttempts = new("Auth.OtpMaxAttempts", "Maximum OTP verification attempts exceeded.");
    public static readonly Error OtpResendCooldown = new("Auth.OtpResendCooldown", "Please wait before requesting a new code.");
    public static readonly Error OtpChallengeNotFound = new("Auth.OtpChallengeNotFound", "OTP challenge not found or expired.");
    public static readonly Error SmsFailed = new("Auth.SmsFailed", "Failed to send SMS.");
    public static readonly Error SsoDisabled = new("Auth.SsoDisabled", "SSO is not enabled.");
    public static readonly Error SsoCodeInvalid = new("Auth.SsoCodeInvalid", "Invalid or expired SSO authorization code.");
    public static readonly Error Unauthorized = new("Auth.Unauthorized", "You are not authenticated.");
    public static readonly Error RoleNotFound = new("Auth.RoleNotFound", "Role not found.");
}
