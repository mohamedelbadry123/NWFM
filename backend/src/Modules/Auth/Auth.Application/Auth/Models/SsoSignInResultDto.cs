namespace Auth.Application.Auth.Models;

public sealed class SsoSignInResultDto
{
    public bool IsGranted { get; init; }
    public string? AuthorizationCode { get; init; }
    public string? DenialReason { get; init; }

    public static SsoSignInResultDto Granted(string code) => new() { IsGranted = true, AuthorizationCode = code };
    public static SsoSignInResultDto Denied(string reason) => new() { IsGranted = false, DenialReason = reason };
}
